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
    /// Blob-backed <see cref="IMixLabMapRepository"/>: library-map job entries live entirely in the
    /// <c>maps/index.json</c> archive index (unlike runs there is no separate per-job manifest —
    /// the index entry *is* the job); the engine payload itself is stored verbatim and unconditioned
    /// at <c>maps/{uploadId}.json</c> so it can be refreshed in place. Claim semantics mirror
    /// <see cref="BlobMixLabRunRepository"/>. See issue #128.
    /// </summary>
    internal sealed class BlobMixLabMapRepository : IMixLabMapRepository
    {
        private const int MaxWriteAttempts = 3;

        private readonly IMixLabBlobGateway _gateway;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<BlobMixLabMapRepository> _logger;

        public BlobMixLabMapRepository(
            IMixLabBlobGateway gateway,
            TimeProvider timeProvider,
            ILogger<BlobMixLabMapRepository> logger)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<MixLabMapJob> RequestAsync(string uploadId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);

            for (int attempt = 1; attempt <= MaxWriteAttempts; attempt++)
            {
                MixLabBlobReadResult? read = await _gateway
                    .ReadAsync(MixLabBlobPaths.MapsIndex, cancellationToken)
                    .ConfigureAwait(false);

                IReadOnlyList<MixLabMapJob> current = read is null
                    ? Array.Empty<MixLabMapJob>()
                    : Deserialize<MixLabMapJob[]>(read.Content);

                MixLabMapJob? existing = current
                    .FirstOrDefault(e => string.Equals(e.UploadId, uploadId, StringComparison.Ordinal));

                if (existing is not null
                    && (existing.Status == MixLabMapStatus.Queued || existing.Status == MixLabMapStatus.Running))
                {
                    // Idempotent: a request for a job already in flight is a no-op — leave the
                    // index untouched and hand back the existing entry.
                    return existing;
                }

                var job = new MixLabMapJob
                {
                    UploadId = uploadId,
                    Status = MixLabMapStatus.Queued,
                    RequestedAt = _timeProvider.GetUtcNow(),
                };

                // Most-recently-requested first, like the uploads index. A prior Succeeded/Failed
                // entry for this uploadId is dropped in favour of the fresh one — that's the
                // refresh path.
                IReadOnlyList<MixLabMapJob> next = new[] { job }
                    .Concat(current.Where(e => !string.Equals(e.UploadId, uploadId, StringComparison.Ordinal)))
                    .ToArray();

                try
                {
                    await _gateway
                        .WriteAsync(MixLabBlobPaths.MapsIndex, Serialize(next), read?.ETag, cancellationToken)
                        .ConfigureAwait(false);
                    return job;
                }
                catch (MixLabConcurrencyException) when (attempt < MaxWriteAttempts)
                {
                    _logger.LogWarning(
                        "MixLab maps index write conflict; re-reading and retrying ({Attempt}/{MaxAttempts}).",
                        attempt,
                        MaxWriteAttempts);
                }
            }

            throw new MixLabConcurrencyException(
                $"Could not update the MixLab maps index after {MaxWriteAttempts} attempts because of concurrent writes.");
        }

        public async Task<MixLabMapJob?> TryClaimOldestQueuedAsync(
            string workerId,
            TimeSpan staleLease,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

            MixLabMapJob? claimed = null;

            await MutateIndexWithRetryAsync(
                current =>
                {
                    DateTimeOffset now = _timeProvider.GetUtcNow();
                    DateTimeOffset threshold = now - staleLease;

                    List<MixLabMapJob> requeued = current
                        .Select(e => e.Status == MixLabMapStatus.Running
                            && e.ClaimedAt is DateTimeOffset claimedAt
                            && claimedAt < threshold
                                ? e with { Status = MixLabMapStatus.Queued, ClaimedAt = null, WorkerId = null }
                                : e)
                        .ToList();

                    MixLabMapJob? oldestQueued = requeued
                        .Where(e => e.Status == MixLabMapStatus.Queued)
                        .OrderBy(e => e.RequestedAt)
                        .FirstOrDefault();

                    if (oldestQueued is null)
                    {
                        claimed = null;
                        return requeued;
                    }

                    MixLabMapJob updated = oldestQueued with
                    {
                        Status = MixLabMapStatus.Running,
                        ClaimedAt = now,
                        WorkerId = workerId,
                    };
                    claimed = updated;

                    return requeued
                        .Select(e => string.Equals(e.UploadId, updated.UploadId, StringComparison.Ordinal) ? updated : e)
                        .ToArray();
                },
                cancellationToken).ConfigureAwait(false);

            return claimed;
        }

        public async Task CompleteAsync(string uploadId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);

            // Payload first: an index write that fails leaves the payload orphaned but harmless
            // (overwritten on the next successful complete); the reverse would mark the job
            // succeeded with no payload behind it.
            await _gateway
                .WriteUnconditionalAsync(MixLabBlobPaths.MapPayload(uploadId), payload, cancellationToken)
                .ConfigureAwait(false);

            DateTimeOffset now = _timeProvider.GetUtcNow();

            await MutateIndexWithRetryAsync(
                current => UpsertStatus(current, uploadId, now, e => e with
                {
                    Status = MixLabMapStatus.Succeeded,
                    CompletedAt = now,
                    Error = null,
                }),
                cancellationToken).ConfigureAwait(false);
        }

        public async Task FailAsync(string uploadId, string error, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);
            ArgumentException.ThrowIfNullOrWhiteSpace(error);

            DateTimeOffset now = _timeProvider.GetUtcNow();

            await MutateIndexWithRetryAsync(
                current => UpsertStatus(current, uploadId, now, e => e with
                {
                    Status = MixLabMapStatus.Failed,
                    CompletedAt = now,
                    Error = error,
                }),
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<MixLabMapJob?> GetJobAsync(string uploadId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);

            IReadOnlyList<MixLabMapJob> entries = await ReadIndexEntriesAsync(cancellationToken).ConfigureAwait(false);
            return entries.FirstOrDefault(e => string.Equals(e.UploadId, uploadId, StringComparison.Ordinal));
        }

        public async Task<byte[]?> OpenPayloadAsync(string uploadId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);

            MixLabBlobReadResult? read = await _gateway
                .ReadAsync(MixLabBlobPaths.MapPayload(uploadId), cancellationToken)
                .ConfigureAwait(false);

            return read?.Content;
        }

        /// <summary>
        /// Updates the entry for <paramref name="uploadId"/> via <paramref name="update"/>, or —
        /// per the behaviour spec — upserts a placeholder first when no entry exists yet, so
        /// <see cref="CompleteAsync"/>/<see cref="FailAsync"/> stay dumb about legality; the use
        /// case layer decides whether completing/failing an unknown upload is itself valid.
        /// </summary>
        private static IReadOnlyList<MixLabMapJob> UpsertStatus(
            IReadOnlyList<MixLabMapJob> current,
            string uploadId,
            DateTimeOffset now,
            Func<MixLabMapJob, MixLabMapJob> update)
        {
            MixLabMapJob? existing = current
                .FirstOrDefault(e => string.Equals(e.UploadId, uploadId, StringComparison.Ordinal));

            if (existing is not null)
            {
                return current
                    .Select(e => string.Equals(e.UploadId, uploadId, StringComparison.Ordinal) ? update(e) : e)
                    .ToArray();
            }

            var placeholder = new MixLabMapJob
            {
                UploadId = uploadId,
                Status = MixLabMapStatus.Queued,
                RequestedAt = now,
            };

            return new[] { update(placeholder) }.Concat(current).ToArray();
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

        private async Task<IReadOnlyList<MixLabMapJob>> ReadIndexEntriesAsync(CancellationToken cancellationToken)
        {
            MixLabBlobReadResult? read = await _gateway
                .ReadAsync(MixLabBlobPaths.MapsIndex, cancellationToken)
                .ConfigureAwait(false);

            return read is null
                ? Array.Empty<MixLabMapJob>()
                : Deserialize<MixLabMapJob[]>(read.Content);
        }

        private async Task MutateIndexWithRetryAsync(
            Func<IReadOnlyList<MixLabMapJob>, IReadOnlyList<MixLabMapJob>> mutate,
            CancellationToken cancellationToken)
        {
            for (int attempt = 1; attempt <= MaxWriteAttempts; attempt++)
            {
                MixLabBlobReadResult? read = await _gateway
                    .ReadAsync(MixLabBlobPaths.MapsIndex, cancellationToken)
                    .ConfigureAwait(false);

                IReadOnlyList<MixLabMapJob> current = read is null
                    ? Array.Empty<MixLabMapJob>()
                    : Deserialize<MixLabMapJob[]>(read.Content);

                IReadOnlyList<MixLabMapJob> next = mutate(current);

                try
                {
                    await _gateway
                        .WriteAsync(MixLabBlobPaths.MapsIndex, Serialize(next), read?.ETag, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }
                catch (MixLabConcurrencyException) when (attempt < MaxWriteAttempts)
                {
                    _logger.LogWarning(
                        "MixLab maps index write conflict; re-reading and retrying ({Attempt}/{MaxAttempts}).",
                        attempt,
                        MaxWriteAttempts);
                }
            }

            throw new MixLabConcurrencyException(
                $"Could not update the MixLab maps index after {MaxWriteAttempts} attempts because of concurrent writes.");
        }
    }
}
