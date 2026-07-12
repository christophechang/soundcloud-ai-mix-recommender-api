using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;
using Changsta.Ai.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Deletes a run and purges its entry from the concept-history document so it stops steering
    /// future runs (novelty penalty + feedback multipliers). Only a non-active run may be deleted;
    /// a queued or running run is rejected so the delete never races an in-flight worker
    /// claim/complete. History is purged first, then the run blobs are removed: both steps are
    /// idempotent, so a caller that retries after a transient failure converges cleanly. The history
    /// document is opaque JSON owned by the Python engine (<c>{"runs": [{"run_id": ...}]}</c>); this
    /// use case is the only place the API reads into it, and it touches nothing but the matching
    /// <c>runs[]</c> entry. See issue #130 and docs/architecture/mixlab-anywhere.md §3.
    /// </summary>
    public sealed class DeleteMixLabRunUseCase : IDeleteMixLabRunUseCase
    {
        private const int MaxHistoryPurgeAttempts = 5;

        // Match the Python engine's 2-space indented history file so a purge does not reflow the
        // whole document. The keys are snake_case (run_id, runs) and are preserved verbatim by
        // JsonNode; only the serializer's whitespace is configured here.
        private static readonly JsonSerializerOptions HistoryJsonOptions = new() { WriteIndented = true };

        private readonly IMixLabRunRepository _runs;
        private readonly IMixLabHistoryStore _history;
        private readonly ILogger<DeleteMixLabRunUseCase> _logger;

        public DeleteMixLabRunUseCase(
            IMixLabRunRepository runs,
            IMixLabHistoryStore history,
            ILogger<DeleteMixLabRunUseCase> logger)
        {
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<DeleteMixLabRunResult> DeleteAsync(string runId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);

            MixLabRun? run = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false);
            if (run is null)
            {
                return new DeleteMixLabRunResult { Outcome = DeleteMixLabRunResult.DeleteOutcome.NotFound };
            }

            if (run.Status is MixLabRunStatus.Queued or MixLabRunStatus.Running)
            {
                return new DeleteMixLabRunResult { Outcome = DeleteMixLabRunResult.DeleteOutcome.Active };
            }

            // Purge history first: if this fails the run is left intact, so the whole delete stays
            // retryable. On retry the purge finds nothing to remove and the run delete completes.
            await PurgeRunFromHistoryAsync(runId, cancellationToken).ConfigureAwait(false);

            await _runs.DeleteAsync(runId, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Deleted MixLab run {RunId} and purged its history entry.", runId);

            return new DeleteMixLabRunResult { Outcome = DeleteMixLabRunResult.DeleteOutcome.Deleted };
        }

        private async Task PurgeRunFromHistoryAsync(string runId, CancellationToken cancellationToken)
        {
            for (int attempt = 1; attempt <= MaxHistoryPurgeAttempts; attempt++)
            {
                MixLabHistorySnapshot? snapshot = await _history.GetAsync(cancellationToken).ConfigureAwait(false);
                if (snapshot is null)
                {
                    // No history document yet — nothing this run could have influenced.
                    return;
                }

                JsonNode? root;
                try
                {
                    root = JsonNode.Parse(snapshot.Content);
                }
                catch (JsonException ex)
                {
                    // The engine owns this schema; if it is unreadable we must not clobber it. Leave
                    // it for the worker's next sync rather than risk destroying history.
                    _logger.LogWarning(ex, "Concept-history document was not valid JSON; skipped purge for run {RunId}.", runId);
                    return;
                }

                if (root is not JsonObject obj || obj["runs"] is not JsonArray runs)
                {
                    return;
                }

                int removed = 0;
                for (int i = runs.Count - 1; i >= 0; i--)
                {
                    if (runs[i] is JsonObject entry
                        && entry.TryGetPropertyValue("run_id", out JsonNode? idNode)
                        && idNode is JsonValue idValue
                        && idValue.TryGetValue(out string? id)
                        && string.Equals(id, runId, StringComparison.Ordinal))
                    {
                        runs.RemoveAt(i);
                        removed++;
                    }
                }

                if (removed == 0)
                {
                    // Nothing to purge (already gone, or the run never reached history) — do not
                    // rewrite the document and churn its ETag for other readers.
                    return;
                }

                try
                {
                    await _history
                        .PutAsync(obj.ToJsonString(HistoryJsonOptions), snapshot.ETag, cancellationToken)
                        .ConfigureAwait(false);

                    _logger.LogInformation(
                        "Purged {Removed} concept-history entr{Suffix} for run {RunId}.",
                        removed,
                        removed == 1 ? "y" : "ies",
                        runId);
                    return;
                }
                catch (MixLabConcurrencyException) when (attempt < MaxHistoryPurgeAttempts)
                {
                    _logger.LogWarning(
                        "Concept-history purge for run {RunId} hit a write conflict; re-reading and retrying ({Attempt}/{MaxAttempts}).",
                        runId,
                        attempt,
                        MaxHistoryPurgeAttempts);
                }
            }

            throw new MixLabConcurrencyException(
                $"Could not purge MixLab run '{runId}' from concept history after {MaxHistoryPurgeAttempts} attempts because of concurrent writes.");
        }
    }
}
