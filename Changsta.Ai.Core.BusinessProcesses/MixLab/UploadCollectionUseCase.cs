using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Normalises an uploaded Rekordbox collection XML to gzip — compressing raw input
    /// server-side, storing already-gzipped input as-is — and delegates storage and index pruning
    /// to <see cref="IMixLabUploadRepository"/>. See docs/architecture/mixlab-anywhere.md §4 row 1
    /// and issue #129.
    /// </summary>
    public sealed class UploadCollectionUseCase : IUploadCollectionUseCase
    {
        private static readonly byte[] GzipMagic = { 0x1F, 0x8B };

        private readonly IMixLabUploadRepository _repository;

        public UploadCollectionUseCase(IMixLabUploadRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<MixLabUpload> UploadAsync(
            Stream body,
            bool contentEncodingSaysGzip,
            string? label,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(body);

            byte[] raw = await ReadAllBytesAsync(body, cancellationToken).ConfigureAwait(false);
            byte[] stored = contentEncodingSaysGzip || LooksLikeGzip(raw) ? raw : Compress(raw);

            return await _repository
                .SaveAsync(new MemoryStream(stored), stored.Length, label, cancellationToken)
                .ConfigureAwait(false);
        }

        private static bool LooksLikeGzip(byte[] content) =>
            content.Length >= GzipMagic.Length && content[0] == GzipMagic[0] && content[1] == GzipMagic[1];

        private static byte[] Compress(byte[] raw)
        {
            using var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true))
            {
                gzip.Write(raw, 0, raw.Length);
            }

            return compressed.ToArray();
        }

        private static async Task<byte[]> ReadAllBytesAsync(Stream body, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer.ToArray();
        }
    }
}
