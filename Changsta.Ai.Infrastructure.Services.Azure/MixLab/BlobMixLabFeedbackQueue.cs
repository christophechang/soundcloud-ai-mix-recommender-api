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
    /// Blob-backed <see cref="IMixLabFeedbackQueue"/> for <c>feedback/pending.json</c>. See
    /// docs/architecture/mixlab-anywhere.md §3 and §4 row 15.
    /// </summary>
    internal sealed class BlobMixLabFeedbackQueue : IMixLabFeedbackQueue
    {
        private const int MaxWriteAttempts = 3;

        private readonly IMixLabBlobGateway _gateway;
        private readonly ILogger<BlobMixLabFeedbackQueue> _logger;

        public BlobMixLabFeedbackQueue(IMixLabBlobGateway gateway, ILogger<BlobMixLabFeedbackQueue> logger)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task AppendAsync(MixLabFeedbackEvent feedbackEvent, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(feedbackEvent);

            return MutateWithRetryAsync(
                current => current.Concat(new[] { feedbackEvent }).ToArray(),
                cancellationToken);
        }

        public Task<IReadOnlyList<MixLabFeedbackEvent>> GetPendingAsync(CancellationToken cancellationToken)
        {
            return ReadEntriesAsync(cancellationToken);
        }

        public Task AckAsync(IReadOnlyList<string> eventIds, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(eventIds);

            var ackedIds = new HashSet<string>(eventIds, StringComparer.Ordinal);

            return MutateWithRetryAsync(
                current => current.Where(e => !ackedIds.Contains(e.EventId)).ToArray(),
                cancellationToken);
        }

        private async Task<IReadOnlyList<MixLabFeedbackEvent>> ReadEntriesAsync(CancellationToken cancellationToken)
        {
            MixLabBlobReadResult? read = await _gateway
                .ReadAsync(MixLabBlobPaths.FeedbackPending, cancellationToken)
                .ConfigureAwait(false);

            return read is null
                ? Array.Empty<MixLabFeedbackEvent>()
                : Deserialize<MixLabFeedbackEvent[]>(read.Content);
        }

        private async Task MutateWithRetryAsync(
            Func<IReadOnlyList<MixLabFeedbackEvent>, IReadOnlyList<MixLabFeedbackEvent>> mutate,
            CancellationToken cancellationToken)
        {
            for (int attempt = 1; attempt <= MaxWriteAttempts; attempt++)
            {
                MixLabBlobReadResult? read = await _gateway
                    .ReadAsync(MixLabBlobPaths.FeedbackPending, cancellationToken)
                    .ConfigureAwait(false);

                IReadOnlyList<MixLabFeedbackEvent> current = read is null
                    ? Array.Empty<MixLabFeedbackEvent>()
                    : Deserialize<MixLabFeedbackEvent[]>(read.Content);

                IReadOnlyList<MixLabFeedbackEvent> next = mutate(current);

                try
                {
                    await _gateway
                        .WriteAsync(MixLabBlobPaths.FeedbackPending, Serialize(next), read?.ETag, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }
                catch (MixLabConcurrencyException) when (attempt < MaxWriteAttempts)
                {
                    _logger.LogWarning(
                        "MixLab feedback queue write conflict; re-reading and retrying ({Attempt}/{MaxAttempts}).",
                        attempt,
                        MaxWriteAttempts);
                }
            }

            throw new MixLabConcurrencyException(
                $"Could not update the MixLab feedback queue after {MaxWriteAttempts} attempts because of concurrent writes.");
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
    }
}
