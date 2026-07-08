using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Persistence for uploaded (gzipped) Rekordbox collection XML and the uploads index. See
    /// docs/architecture/mixlab-anywhere.md §3 and §4 rows 1-3.
    /// </summary>
    public interface IMixLabUploadRepository
    {
        /// <summary>
        /// Stores <paramref name="gzipContent"/> and prunes the uploads index to the 5 most
        /// recent uploads, deleting the blobs for any pruned entries.
        /// </summary>
        Task<MixLabUpload> SaveAsync(Stream gzipContent, long sizeBytes, string? label, CancellationToken cancellationToken);

        Task<string?> GetLatestIdAsync(CancellationToken cancellationToken);

        Task<Stream> OpenReadAsync(string uploadId, CancellationToken cancellationToken);

        Task<IReadOnlyList<MixLabUpload>> GetIndexAsync(CancellationToken cancellationToken);
    }
}
