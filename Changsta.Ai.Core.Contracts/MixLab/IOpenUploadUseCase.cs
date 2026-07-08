using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Resolves and opens a stored upload for download, including the literal id <c>latest</c>. See
    /// docs/architecture/mixlab-anywhere.md §4 row 3 and issue #129.
    /// </summary>
    public interface IOpenUploadUseCase
    {
        /// <summary>
        /// Returns <see langword="null"/> when <paramref name="uploadId"/> does not resolve to a
        /// stored upload — including when <paramref name="uploadId"/> is the literal <c>latest</c>
        /// and there are no uploads yet.
        /// </summary>
        Task<MixLabUploadContent?> OpenAsync(string uploadId, CancellationToken cancellationToken);
    }
}
