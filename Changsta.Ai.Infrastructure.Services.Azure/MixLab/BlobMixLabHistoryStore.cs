using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;

namespace Changsta.Ai.Infrastructure.Services.Azure.MixLab
{
    /// <summary>
    /// Blob-backed <see cref="IMixLabHistoryStore"/> for <c>history/concept-history.json</c>. The
    /// content is opaque (it mirrors the Python engine's history schema); this store only handles
    /// the ETag round trip. See docs/architecture/mixlab-anywhere.md §3 and §4 row 13.
    /// </summary>
    internal sealed class BlobMixLabHistoryStore : IMixLabHistoryStore
    {
        private readonly IMixLabBlobGateway _gateway;

        public BlobMixLabHistoryStore(IMixLabBlobGateway gateway)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        }

        public async Task<MixLabHistorySnapshot?> GetAsync(CancellationToken cancellationToken)
        {
            MixLabBlobReadResult? read = await _gateway
                .ReadAsync(MixLabBlobPaths.HistoryDocument, cancellationToken)
                .ConfigureAwait(false);

            return read is null
                ? null
                : new MixLabHistorySnapshot(Encoding.UTF8.GetString(read.Content), read.ETag);
        }

        public Task<string> PutAsync(string content, string? ifMatchETag, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(content);

            return _gateway.WriteAsync(
                MixLabBlobPaths.HistoryDocument,
                Encoding.UTF8.GetBytes(content),
                ifMatchETag,
                cancellationToken);
        }
    }
}
