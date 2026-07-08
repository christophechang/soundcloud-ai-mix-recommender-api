using System;
using System.Collections.Generic;
using System.IO;
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
    /// Blob-backed <see cref="IMixLabUploadRepository"/>: gzipped upload content at
    /// <c>uploads/{uploadId}.xml.gz</c> plus the <c>uploads/index.json</c> archive index, pruned
    /// to the 5 most recent uploads on every save. See docs/architecture/mixlab-anywhere.md §3 and
    /// §4 rows 1-3, and issue #128.
    /// </summary>
    internal sealed class BlobMixLabUploadRepository : IMixLabUploadRepository
    {
        private const int MaxWriteAttempts = 3;
        private const int MaxRetainedUploads = 5;

        private readonly IMixLabBlobGateway _gateway;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<BlobMixLabUploadRepository> _logger;

        public BlobMixLabUploadRepository(
            IMixLabBlobGateway gateway,
            TimeProvider timeProvider,
            ILogger<BlobMixLabUploadRepository> logger)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<MixLabUpload> SaveAsync(
            Stream gzipContent,
            long sizeBytes,
            string? label,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(gzipContent);

            DateTimeOffset now = _timeProvider.GetUtcNow();
            string uploadId = NewUploadId(now);

            await _gateway
                .WriteStreamAsync(MixLabBlobPaths.UploadContent(uploadId), gzipContent, cancellationToken)
                .ConfigureAwait(false);

            var upload = new MixLabUpload
            {
                UploadId = uploadId,
                UploadedAt = now,
                SizeBytes = sizeBytes,
                Label = label,
            };

            IReadOnlyList<MixLabUpload> pruned = Array.Empty<MixLabUpload>();

            await MutateIndexWithRetryAsync(
                current =>
                {
                    List<MixLabUpload> next = new[] { upload }.Concat(current).ToList();
                    if (next.Count > MaxRetainedUploads)
                    {
                        pruned = next.Skip(MaxRetainedUploads).ToArray();
                        next = next.Take(MaxRetainedUploads).ToList();
                    }
                    else
                    {
                        pruned = Array.Empty<MixLabUpload>();
                    }

                    return next;
                },
                cancellationToken).ConfigureAwait(false);

            foreach (MixLabUpload prunedUpload in pruned)
            {
                await _gateway
                    .DeleteAsync(MixLabBlobPaths.UploadContent(prunedUpload.UploadId), cancellationToken)
                    .ConfigureAwait(false);
                _logger.LogInformation(
                    "Pruned MixLab upload {UploadId} beyond retention of {MaxRetained}.",
                    prunedUpload.UploadId,
                    MaxRetainedUploads);
            }

            return upload;
        }

        public async Task<string?> GetLatestIdAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<MixLabUpload> entries = await ReadIndexEntriesAsync(cancellationToken).ConfigureAwait(false);
            return entries.Count == 0 ? null : entries[0].UploadId;
        }

        public Task<Stream> OpenReadAsync(string uploadId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);
            return _gateway.OpenReadStreamAsync(MixLabBlobPaths.UploadContent(uploadId), cancellationToken);
        }

        public Task<IReadOnlyList<MixLabUpload>> GetIndexAsync(CancellationToken cancellationToken)
        {
            return ReadIndexEntriesAsync(cancellationToken);
        }

        private static string NewUploadId(DateTimeOffset now)
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 4);
            return $"u_{now:yyyyMMdd}_{suffix}";
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

        private async Task<IReadOnlyList<MixLabUpload>> ReadIndexEntriesAsync(CancellationToken cancellationToken)
        {
            MixLabBlobReadResult? read = await _gateway
                .ReadAsync(MixLabBlobPaths.UploadsIndex, cancellationToken)
                .ConfigureAwait(false);

            return read is null
                ? Array.Empty<MixLabUpload>()
                : Deserialize<MixLabUpload[]>(read.Content);
        }

        private async Task MutateIndexWithRetryAsync(
            Func<IReadOnlyList<MixLabUpload>, IReadOnlyList<MixLabUpload>> mutate,
            CancellationToken cancellationToken)
        {
            for (int attempt = 1; attempt <= MaxWriteAttempts; attempt++)
            {
                MixLabBlobReadResult? read = await _gateway
                    .ReadAsync(MixLabBlobPaths.UploadsIndex, cancellationToken)
                    .ConfigureAwait(false);

                IReadOnlyList<MixLabUpload> current = read is null
                    ? Array.Empty<MixLabUpload>()
                    : Deserialize<MixLabUpload[]>(read.Content);

                IReadOnlyList<MixLabUpload> next = mutate(current);

                try
                {
                    await _gateway
                        .WriteAsync(MixLabBlobPaths.UploadsIndex, Serialize(next), read?.ETag, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }
                catch (MixLabConcurrencyException) when (attempt < MaxWriteAttempts)
                {
                    _logger.LogWarning(
                        "MixLab uploads index write conflict; re-reading and retrying ({Attempt}/{MaxAttempts}).",
                        attempt,
                        MaxWriteAttempts);
                }
            }

            throw new MixLabConcurrencyException(
                $"Could not update the MixLab uploads index after {MaxWriteAttempts} attempts because of concurrent writes.");
        }

    }
}
