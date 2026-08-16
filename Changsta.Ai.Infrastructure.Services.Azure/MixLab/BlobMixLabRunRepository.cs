using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;
using Changsta.Ai.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Infrastructure.Services.Azure.MixLab
{
    /// <summary>
    /// Blob-backed <see cref="IMixLabRunRepository"/>: run manifests at
    /// <c>runs/{runId}/run.json</c> plus the <c>runs/index.json</c> archive index, both under
    /// ETag optimistic concurrency with bounded re-read/retry — mirroring the pattern documented
    /// on <see cref="Changsta.Ai.Infrastructure.Services.Azure.Catalogue.BlobCatalogMixDeleter"/>.
    /// Claim semantics follow docs/architecture/mixlab-anywhere.md §4 row 10. See issue #128.
    /// </summary>
    internal sealed class BlobMixLabRunRepository : IMixLabRunRepository
    {
        private const int MaxWriteAttempts = 3;

        // Every per-run artifact the pipeline writes (see CompleteMixLabRunUseCase). A run delete
        // removes these plus run.json; any new artifact kind must be added here so a delete stays
        // complete — the blob gateway has no prefix-delete.
        private static readonly string[] RunArtifactNames = { "summary.json", "report.html", "export.xml" };

        private readonly IMixLabBlobGateway _gateway;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<BlobMixLabRunRepository> _logger;

        public BlobMixLabRunRepository(
            IMixLabBlobGateway gateway,
            TimeProvider timeProvider,
            ILogger<BlobMixLabRunRepository> logger)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<MixLabRunIndexEntry>> GetIndexAsync(int take, int skip, CancellationToken cancellationToken)
        {
            IReadOnlyList<MixLabRunIndexEntry> entries = await ReadIndexEntriesAsync(cancellationToken).ConfigureAwait(false);
            return entries.Skip(skip).Take(take).ToArray();
        }

        public async Task<MixLabRun?> GetAsync(string runId, CancellationToken cancellationToken)
        {
            MixLabBlobReadResult? read = await _gateway
                .ReadAsync(MixLabBlobPaths.RunManifest(runId), cancellationToken)
                .ConfigureAwait(false);

            return read is null ? null : Deserialize<MixLabRun>(read.Content);
        }

        public async Task<MixLabRun> CreateQueuedAsync(MixLabRunFlags flags, string uploadId, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(flags);
            ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);

            DateTimeOffset now = _timeProvider.GetUtcNow();
            string runId = NewRunId(now);

            var run = new MixLabRun
            {
                RunId = runId,
                CreatedAt = now,
                Status = MixLabRunStatus.Queued,
                Flags = flags,
                UploadId = uploadId,
            };

            await _gateway
                .WriteAsync(MixLabBlobPaths.RunManifest(runId), Serialize(run), expectedETag: null, cancellationToken)
                .ConfigureAwait(false);

            var indexEntry = new MixLabRunIndexEntry
            {
                RunId = runId,
                CreatedAt = now,
                Status = MixLabRunStatus.Queued,
                Genre = flags.Genre,
                FlagsSummary = BuildFlagsSummary(flags),
                ConceptCount = 0,
            };

            await MutateRunIndexWithRetryAsync(
                entries => new[] { indexEntry }.Concat(entries).ToArray(),
                cancellationToken).ConfigureAwait(false);

            return run;
        }

        public async Task<bool> HasLiveRunningRunAsync(TimeSpan staleLease, CancellationToken cancellationToken)
        {
            IReadOnlyList<MixLabRunIndexEntry> entries = await ReadIndexEntriesAsync(cancellationToken).ConfigureAwait(false);
            DateTimeOffset threshold = _timeProvider.GetUtcNow() - staleLease;

            foreach (MixLabRunIndexEntry entry in entries.Where(e => e.Status == MixLabRunStatus.Running))
            {
                // The index carries no claimedAt, so liveness is read from the manifest — the same
                // source RequeueIfStaleAsync judges staleness from, with the same comparison.
                MixLabRun? run = await GetAsync(entry.RunId, cancellationToken).ConfigureAwait(false);

                if (run is not null
                    && run.Status == MixLabRunStatus.Running
                    && run.ClaimedAt is DateTimeOffset claimedAt
                    && claimedAt >= threshold)
                {
                    return true;
                }
            }

            return false;
        }

        public async Task<MixLabRun?> TryClaimOldestQueuedAsync(string workerId, TimeSpan staleLease, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

            for (int attempt = 1; attempt <= MaxWriteAttempts; attempt++)
            {
                await RequeueStaleRunningAsync(staleLease, cancellationToken).ConfigureAwait(false);

                IReadOnlyList<MixLabRunIndexEntry> entries = await ReadIndexEntriesAsync(cancellationToken).ConfigureAwait(false);

                MixLabRunIndexEntry? oldestQueued = entries
                    .Where(e => e.Status == MixLabRunStatus.Queued)
                    .OrderBy(e => e.CreatedAt)
                    .FirstOrDefault();

                if (oldestQueued is null)
                {
                    return null;
                }

                MixLabBlobReadResult? read = await _gateway
                    .ReadAsync(MixLabBlobPaths.RunManifest(oldestQueued.RunId), cancellationToken)
                    .ConfigureAwait(false);

                if (read is null)
                {
                    continue;
                }

                MixLabRun current = Deserialize<MixLabRun>(read.Content);
                if (current.Status != MixLabRunStatus.Queued)
                {
                    // Lost the race to another claimant between the index read and here; retry.
                    continue;
                }

                DateTimeOffset now = _timeProvider.GetUtcNow();
                MixLabRun claimed = current with
                {
                    Status = MixLabRunStatus.Running,
                    ClaimedAt = now,
                    WorkerId = workerId,
                };

                try
                {
                    await _gateway
                        .WriteAsync(MixLabBlobPaths.RunManifest(claimed.RunId), Serialize(claimed), read.ETag, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (MixLabConcurrencyException)
                {
                    // Another worker claimed it first; re-read the index and try again.
                    continue;
                }

                await MutateRunIndexWithRetryAsync(
                    indexEntries => indexEntries
                        .Select(e => string.Equals(e.RunId, claimed.RunId, StringComparison.Ordinal)
                            ? e with { Status = MixLabRunStatus.Running }
                            : e)
                        .ToArray(),
                    cancellationToken).ConfigureAwait(false);

                return claimed;
            }

            throw new MixLabConcurrencyException(
                $"Could not claim a queued MixLab run after {MaxWriteAttempts} attempts because of concurrent writes.");
        }

        public async Task CompleteAsync(string runId, IReadOnlyList<MixLabRunConcept> concepts, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);
            ArgumentNullException.ThrowIfNull(concepts);

            for (int attempt = 1; attempt <= MaxWriteAttempts; attempt++)
            {
                MixLabBlobReadResult? read = await _gateway
                    .ReadAsync(MixLabBlobPaths.RunManifest(runId), cancellationToken)
                    .ConfigureAwait(false);

                if (read is null)
                {
                    throw new MixLabInvalidRunStateException(runId, $"MixLab run '{runId}' does not exist.");
                }

                MixLabRun current = Deserialize<MixLabRun>(read.Content);

                if (current.Status == MixLabRunStatus.Succeeded)
                {
                    // Idempotent: a repeat complete for an already-succeeded run is a no-op. See
                    // docs/architecture/mixlab-anywhere.md §4 row 11 and §8.
                    return;
                }

                if (current.Status != MixLabRunStatus.Running)
                {
                    throw new MixLabInvalidRunStateException(
                        runId,
                        $"Cannot complete MixLab run '{runId}' from status '{current.Status}'; expected 'Running'.");
                }

                DateTimeOffset now = _timeProvider.GetUtcNow();
                MixLabRun completed = current with
                {
                    Status = MixLabRunStatus.Succeeded,
                    CompletedAt = now,
                    Concepts = concepts,
                };

                try
                {
                    await _gateway
                        .WriteAsync(MixLabBlobPaths.RunManifest(runId), Serialize(completed), read.ETag, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (MixLabConcurrencyException) when (attempt < MaxWriteAttempts)
                {
                    _logger.LogWarning(
                        "Complete of MixLab run {RunId} hit a write conflict; re-reading and retrying ({Attempt}/{MaxAttempts}).",
                        runId,
                        attempt,
                        MaxWriteAttempts);
                    continue;
                }

                await MutateRunIndexWithRetryAsync(
                    entries => entries
                        .Select(e => string.Equals(e.RunId, runId, StringComparison.Ordinal)
                            ? e with { Status = MixLabRunStatus.Succeeded, ConceptCount = concepts.Count }
                            : e)
                        .ToArray(),
                    cancellationToken).ConfigureAwait(false);

                return;
            }

            throw new MixLabConcurrencyException(
                $"Could not complete MixLab run '{runId}' after {MaxWriteAttempts} attempts because of concurrent writes.");
        }

        public async Task FailAsync(string runId, string error, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);
            ArgumentException.ThrowIfNullOrWhiteSpace(error);

            for (int attempt = 1; attempt <= MaxWriteAttempts; attempt++)
            {
                MixLabBlobReadResult? read = await _gateway
                    .ReadAsync(MixLabBlobPaths.RunManifest(runId), cancellationToken)
                    .ConfigureAwait(false);

                if (read is null)
                {
                    throw new MixLabInvalidRunStateException(runId, $"MixLab run '{runId}' does not exist.");
                }

                MixLabRun current = Deserialize<MixLabRun>(read.Content);
                DateTimeOffset now = _timeProvider.GetUtcNow();
                MixLabRun failed = current with
                {
                    Status = MixLabRunStatus.Failed,
                    CompletedAt = now,
                    Error = error,
                };

                try
                {
                    await _gateway
                        .WriteAsync(MixLabBlobPaths.RunManifest(runId), Serialize(failed), read.ETag, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (MixLabConcurrencyException) when (attempt < MaxWriteAttempts)
                {
                    continue;
                }

                await MutateRunIndexWithRetryAsync(
                    entries => entries
                        .Select(e => string.Equals(e.RunId, runId, StringComparison.Ordinal)
                            ? e with { Status = MixLabRunStatus.Failed }
                            : e)
                        .ToArray(),
                    cancellationToken).ConfigureAwait(false);

                return;
            }

            throw new MixLabConcurrencyException(
                $"Could not fail MixLab run '{runId}' after {MaxWriteAttempts} attempts because of concurrent writes.");
        }

        public async Task UpdateConceptFeedbackAsync(
            string runId,
            string conceptId,
            MixLabConceptFeedback feedback,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(feedback);

            await MutateConceptWithRetryAsync(
                runId,
                conceptId,
                "feedback",
                c => c with { Feedback = feedback },
                cancellationToken).ConfigureAwait(false);
        }

        public async Task UpdateConceptShortlistAsync(
            string runId,
            string conceptId,
            bool shortlisted,
            CancellationToken cancellationToken)
        {
            await MutateConceptWithRetryAsync(
                runId,
                conceptId,
                "shortlist",
                c => c with { Shortlisted = shortlisted },
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> RecomputeIndexCountsAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<MixLabRunIndexEntry> entries = await ReadIndexEntriesAsync(cancellationToken).ConfigureAwait(false);

            if (entries.Count == 0)
            {
                // Nothing to project from: skip the index write entirely rather than rewriting the
                // blob (and burning an ETag) to store what it already holds.
                return 0;
            }

            // One blob GET per run, sequentially. At the current archive size (tens of runs) that is
            // far inside App Service's 230-second request cap; introduce bounded concurrency before
            // the archive reaches the low hundreds. The sweep is not a snapshot: a shortlist or
            // feedback write that lands after this loop read that run's manifest is clobbered by the
            // numbers below and stays wrong until the next reindex. Harmless for a manual repair
            // tool — do not put this on a timer, where it would race the live writes continuously.
            var counts = new Dictionary<string, (int ShortlistedCount, int PlayedCount)>(StringComparer.Ordinal);

            foreach (MixLabRunIndexEntry entry in entries)
            {
                MixLabRun? run = await GetAsync(entry.RunId, cancellationToken).ConfigureAwait(false);

                if (run is not null)
                {
                    counts[entry.RunId] = CountConcepts(run);
                }
            }

            if (counts.Count == 0)
            {
                // Same reasoning as the empty-index early-out above: every manifest was missing, so
                // the projection below would rewrite the index byte-for-byte. Skip the write.
                return 0;
            }

            // Apply with `with` on whatever the index holds at write time — never write back the
            // array read above, because a worker claim/complete may have changed a status meanwhile.
            await MutateRunIndexWithRetryAsync(
                current => current
                    .Select(e => counts.TryGetValue(e.RunId, out (int ShortlistedCount, int PlayedCount) c)
                        ? e with { ShortlistedCount = c.ShortlistedCount, PlayedCount = c.PlayedCount }
                        : e)
                    .ToArray(),
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Recomputed MixLab index counts for {RunCount} run(s).", counts.Count);

            return counts.Count;
        }

        public async Task DeleteAsync(string runId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);

            // Manifest + artifacts. DeleteAsync is delete-if-exists, so an absent optional artifact
            // (e.g. export.xml for a run that produced no concepts) is a harmless no-op.
            await _gateway.DeleteAsync(MixLabBlobPaths.RunManifest(runId), cancellationToken).ConfigureAwait(false);
            foreach (string artifact in RunArtifactNames)
            {
                await _gateway
                    .DeleteAsync(MixLabBlobPaths.RunArtifact(runId, artifact), cancellationToken)
                    .ConfigureAwait(false);
            }

            // Drop the archive-index entry. Idempotent: filtering out a run id that is not present
            // leaves the index unchanged.
            await MutateRunIndexWithRetryAsync(
                entries => entries
                    .Where(e => !string.Equals(e.RunId, runId, StringComparison.Ordinal))
                    .ToArray(),
                cancellationToken).ConfigureAwait(false);
        }

        private static string NewRunId(DateTimeOffset now)
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 4);
            return $"r_{now:yyyyMMdd}_{suffix}";
        }

        private static string BuildFlagsSummary(MixLabRunFlags flags)
        {
            string summary = $"{flags.Mode}/{flags.Risk}/{flags.Directions}";
            return string.IsNullOrEmpty(flags.TrackPool) ? summary : $"{summary}/block";
        }

        private static ReadOnlyMemory<byte> Serialize<T>(T value)
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, MixLabJsonOptions.Options);
        }

        private static T Deserialize<T>(byte[] content)
        {
            return JsonSerializer.Deserialize<T>(content, MixLabJsonOptions.Options)
                ?? throw new JsonException($"MixLab blob content deserialised to null for type {typeof(T).Name}.");
        }

        /// <summary>
        /// The manifest-derived counts an index entry projects: starred concepts, and concepts whose
        /// feedback verdict is played or played-modified.
        /// </summary>
        private static (int ShortlistedCount, int PlayedCount) CountConcepts(MixLabRun run)
        {
            int shortlisted = run.Concepts.Count(c => c.Shortlisted);
            int played = run.Concepts.Count(c =>
                c.Feedback is not null
                && (c.Feedback.Verdict == MixLabFeedbackVerdict.Played
                    || c.Feedback.Verdict == MixLabFeedbackVerdict.PlayedModified));

            return (shortlisted, played);
        }

        /// <summary>
        /// Read-modify-write of a single concept inside a run manifest under ETag <c>If-Match</c>
        /// with bounded retry, followed by the best-effort index-count refresh. The concept-level
        /// writes (feedback, shortlist) differ only in which field <paramref name="mutate"/> sets and
        /// in the <paramref name="operation"/> word their exhausted-retry message carries.
        /// Throws <see cref="MixLabInvalidRunStateException"/> when the run or the concept does not
        /// exist, and <see cref="MixLabConcurrencyException"/> when the manifest write never lands.
        /// </summary>
        private async Task MutateConceptWithRetryAsync(
            string runId,
            string conceptId,
            string operation,
            Func<MixLabRunConcept, MixLabRunConcept> mutate,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);
            ArgumentException.ThrowIfNullOrWhiteSpace(conceptId);

            for (int attempt = 1; attempt <= MaxWriteAttempts; attempt++)
            {
                MixLabBlobReadResult? read = await _gateway
                    .ReadAsync(MixLabBlobPaths.RunManifest(runId), cancellationToken)
                    .ConfigureAwait(false);

                if (read is null)
                {
                    throw new MixLabInvalidRunStateException(runId, $"MixLab run '{runId}' does not exist.");
                }

                MixLabRun current = Deserialize<MixLabRun>(read.Content);

                if (!current.Concepts.Any(c => string.Equals(c.ConceptId, conceptId, StringComparison.Ordinal)))
                {
                    throw new MixLabInvalidRunStateException(
                        runId,
                        $"Concept '{conceptId}' was not found on MixLab run '{runId}'.");
                }

                MixLabRun updated = current with
                {
                    Concepts = current.Concepts
                        .Select(c => string.Equals(c.ConceptId, conceptId, StringComparison.Ordinal)
                            ? mutate(c)
                            : c)
                        .ToArray(),
                };

                try
                {
                    await _gateway
                        .WriteAsync(MixLabBlobPaths.RunManifest(runId), Serialize(updated), read.ETag, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (MixLabConcurrencyException) when (attempt < MaxWriteAttempts)
                {
                    continue;
                }

                await RefreshIndexCountsAsync(runId, cancellationToken).ConfigureAwait(false);

                return;
            }

            throw new MixLabConcurrencyException(
                $"Could not update concept {operation} on MixLab run '{runId}' after {MaxWriteAttempts} attempts because of concurrent writes.");
        }

        private async Task RequeueStaleRunningAsync(TimeSpan staleLease, CancellationToken cancellationToken)
        {
            IReadOnlyList<MixLabRunIndexEntry> entries = await ReadIndexEntriesAsync(cancellationToken).ConfigureAwait(false);
            DateTimeOffset threshold = _timeProvider.GetUtcNow() - staleLease;

            IEnumerable<string> runningRunIds = entries
                .Where(e => e.Status == MixLabRunStatus.Running)
                .Select(e => e.RunId);

            foreach (string runId in runningRunIds)
            {
                await RequeueIfStaleAsync(runId, threshold, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task RequeueIfStaleAsync(string runId, DateTimeOffset threshold, CancellationToken cancellationToken)
        {
            for (int attempt = 1; attempt <= MaxWriteAttempts; attempt++)
            {
                MixLabBlobReadResult? read = await _gateway
                    .ReadAsync(MixLabBlobPaths.RunManifest(runId), cancellationToken)
                    .ConfigureAwait(false);

                if (read is null)
                {
                    return;
                }

                MixLabRun current = Deserialize<MixLabRun>(read.Content);

                if (current.Status != MixLabRunStatus.Running
                    || current.ClaimedAt is not DateTimeOffset claimedAt
                    || claimedAt >= threshold)
                {
                    return;
                }

                MixLabRun requeued = current with
                {
                    Status = MixLabRunStatus.Queued,
                    ClaimedAt = null,
                    WorkerId = null,
                };

                try
                {
                    await _gateway
                        .WriteAsync(MixLabBlobPaths.RunManifest(runId), Serialize(requeued), read.ETag, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (MixLabConcurrencyException) when (attempt < MaxWriteAttempts)
                {
                    continue;
                }

                await MutateRunIndexWithRetryAsync(
                    entries => entries
                        .Select(e => string.Equals(e.RunId, runId, StringComparison.Ordinal)
                            ? e with { Status = MixLabRunStatus.Queued }
                            : e)
                        .ToArray(),
                    cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("Requeued stale MixLab run {RunId} (claim lease expired).", runId);
                return;
            }
        }

        /// <summary>
        /// Re-derives one run's index counts from its manifest. The manifest read happens inside the
        /// index mutate delegate, so it repeats on every retry attempt — see
        /// <see cref="MutateRunIndexWithRetryAsync(Func{IReadOnlyList{MixLabRunIndexEntry}, CancellationToken, Task{IReadOnlyList{MixLabRunIndexEntry}}}, CancellationToken)"/>.
        /// Manifest and index are separate blobs with separate ETags, so a crash between the two
        /// writes leaves the counts stale; <see cref="RecomputeIndexCountsAsync"/> is the repair.
        /// <para>
        /// Best-effort by design, and unconditionally so apart from cancellation: the caller's real
        /// write (the manifest) has already landed by the time this runs, so <em>any</em> index
        /// failure — a lost ETag race, a blob-service 500/503, a transport fault — is logged and the
        /// counts left stale rather than surfaced as a failure for an operation that succeeded. Only
        /// <see cref="OperationCanceledException"/> propagates.
        /// </para>
        /// </summary>
        private async Task RefreshIndexCountsAsync(string runId, CancellationToken cancellationToken)
        {
            try
            {
                await MutateRunIndexWithRetryAsync(
                    async (entries, ct) =>
                    {
                        if (!entries.Any(e => string.Equals(e.RunId, runId, StringComparison.Ordinal)))
                        {
                            return entries;
                        }

                        MixLabRun? run = await GetAsync(runId, ct).ConfigureAwait(false);

                        if (run is null)
                        {
                            return entries;
                        }

                        (int shortlistedCount, int playedCount) = CountConcepts(run);

                        return entries
                            .Select(e => string.Equals(e.RunId, runId, StringComparison.Ordinal)
                                ? e with { ShortlistedCount = shortlistedCount, PlayedCount = playedCount }
                                : e)
                            .ToArray();
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Deliberately broad: the failure mode is not only a lost ETag race. A 500/503, a
                // socket reset, or a malformed index blob would otherwise escape this guard and fail
                // a write whose manifest has already landed. Cancellation still propagates — the
                // caller is going away, and swallowing it would hide that.
                _logger.LogWarning(
                    ex,
                    "Index counts for {RunId} left stale after exhausting index write retries; POST runs/reindex repairs.",
                    runId);
            }
        }

        private async Task<IReadOnlyList<MixLabRunIndexEntry>> ReadIndexEntriesAsync(CancellationToken cancellationToken)
        {
            MixLabBlobReadResult? read = await _gateway
                .ReadAsync(MixLabBlobPaths.RunsIndex, cancellationToken)
                .ConfigureAwait(false);

            return read is null
                ? Array.Empty<MixLabRunIndexEntry>()
                : Deserialize<MixLabRunIndexEntry[]>(read.Content);
        }

        private Task MutateRunIndexWithRetryAsync(
            Func<IReadOnlyList<MixLabRunIndexEntry>, IReadOnlyList<MixLabRunIndexEntry>> mutate,
            CancellationToken cancellationToken)
        {
            return MutateRunIndexWithRetryAsync(
                (entries, _) => Task.FromResult(mutate(entries)),
                cancellationToken);
        }

        /// <summary>
        /// Read-modify-write of <c>runs/index.json</c> under ETag <c>If-Match</c> with bounded
        /// retry. The mutate delegate runs afresh on every attempt, which is what makes the derived
        /// counts converge: a count refresh re-reads the run manifest inside the delegate, and a
        /// competing writer always writes its manifest before its index, so the attempt that lost
        /// the ETag race also sees the competitor's manifest.
        /// </summary>
        private async Task MutateRunIndexWithRetryAsync(
            Func<IReadOnlyList<MixLabRunIndexEntry>, CancellationToken, Task<IReadOnlyList<MixLabRunIndexEntry>>> mutateAsync,
            CancellationToken cancellationToken)
        {
            for (int attempt = 1; attempt <= MaxWriteAttempts; attempt++)
            {
                MixLabBlobReadResult? read = await _gateway
                    .ReadAsync(MixLabBlobPaths.RunsIndex, cancellationToken)
                    .ConfigureAwait(false);

                IReadOnlyList<MixLabRunIndexEntry> current = read is null
                    ? Array.Empty<MixLabRunIndexEntry>()
                    : Deserialize<MixLabRunIndexEntry[]>(read.Content);

                IReadOnlyList<MixLabRunIndexEntry> next = await mutateAsync(current, cancellationToken).ConfigureAwait(false);

                try
                {
                    await _gateway
                        .WriteAsync(MixLabBlobPaths.RunsIndex, Serialize(next), read?.ETag, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }
                catch (MixLabConcurrencyException) when (attempt < MaxWriteAttempts)
                {
                    _logger.LogWarning(
                        "MixLab runs index write conflict; re-reading and retrying ({Attempt}/{MaxAttempts}).",
                        attempt,
                        MaxWriteAttempts);
                }
            }

            throw new MixLabConcurrencyException(
                $"Could not update the MixLab runs index after {MaxWriteAttempts} attempts because of concurrent writes.");
        }
    }
}
