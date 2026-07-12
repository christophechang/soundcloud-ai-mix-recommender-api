using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Writes the raw <c>history/concept-history.json</c> document: validates only that the body is
    /// well-formed JSON (the document's schema is owned by the Python engine) and honours the
    /// caller's <c>If-Match</c> ETag. Backs <c>PUT /api/mixlab/history</c>. See
    /// docs/architecture/mixlab-anywhere.md §4 row 13 and issue #131.
    /// </summary>
    public interface IPutMixLabHistoryUseCase
    {
        /// <summary>
        /// <paramref name="ifMatchETag"/> is <see langword="null"/> for a first write (no prior
        /// document). The caller maps <see cref="PutMixLabHistoryResult.Outcome"/> to a transport
        /// status code.
        /// </summary>
        Task<PutMixLabHistoryResult> PutAsync(string content, string? ifMatchETag, CancellationToken cancellationToken);
    }
}
