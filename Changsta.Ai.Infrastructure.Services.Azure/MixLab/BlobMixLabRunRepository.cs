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
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);
            ArgumentException.ThrowIfNullOrWhiteSpace(conceptId);
            ArgumentNullException.ThrowIfNull(feedback);

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
                            ? c with { Feedback = feedback }
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

                return;
            }

            throw new MixLabConcurrencyException(
                $"Could not update concept feedback on MixLab run '{runId}' after {MaxWriteAttempts} attempts because of concurrent writes.");
        }

        private static string NewRunId(DateTimeOffset now)
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 4);
            return $"r_{now:yyyyMMdd}_{suffix}";
        }

        private static string BuildFlagsSummary(MixLabRunFlags flags)
        {
            return $"{flags.Mode}/{flags.Risk}/{flags.Directions}";
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

        private async Task<IReadOnlyList<MixLabRunIndexEntry>> ReadIndexEntriesAsync(CancellationToken cancellationToken)
        {
            MixLabBlobReadResult? read = await _gateway
                .ReadAsync(MixLabBlobPaths.RunsIndex, cancellationToken)
                .ConfigureAwait(false);

            return read is null
                ? Array.Empty<MixLabRunIndexEntry>()
                : Deserialize<MixLabRunIndexEntry[]>(read.Content);
        }

        private async Task MutateRunIndexWithRetryAsync(
            Func<IReadOnlyList<MixLabRunIndexEntry>, IReadOnlyList<MixLabRunIndexEntry>> mutate,
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

                IReadOnlyList<MixLabRunIndexEntry> next = mutate(current);

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
