using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Orchestrates a Rekordbox collection XML upload: normalises the payload to gzip and
    /// delegates storage and index pruning to <see cref="IMixLabUploadRepository"/>. See
    /// docs/architecture/mixlab-anywhere.md §4 row 1 and issue #129.
    /// </summary>
    public interface IUploadCollectionUseCase
    {
        /// <summary>
        /// Stores <paramref name="body"/> as a gzipped upload. When <paramref name="contentEncodingSaysGzip"/>
        /// is <see langword="false"/>, the body is sniffed for the gzip magic bytes (0x1F 0x8B) at
        /// its head; if neither the header nor the sniff indicate gzip, the body is compressed
        /// server-side before storage. Already-gzipped input (by header or sniff) is stored as-is.
        /// </summary>
        Task<MixLabUpload> UploadAsync(
            Stream body,
            bool contentEncodingSaysGzip,
            string? label,
            CancellationToken cancellationToken);
    }
}
