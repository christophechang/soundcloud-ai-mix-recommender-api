using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Persistence for <c>history/concept-history.json</c>, the canonical engine history mirrored
    /// between the API and the Mac mini worker. See docs/architecture/mixlab-anywhere.md §3 and §4
    /// row 13.
    /// </summary>
    public interface IMixLabHistoryStore
    {
        /// <summary>Returns <see langword="null"/> when the history document does not exist yet.</summary>
        Task<MixLabHistorySnapshot?> GetAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Writes the history document. When <paramref name="ifMatchETag"/> is non-null the write
        /// is conditioned on <c>If-Match</c>; when null the write is create-only. Returns the new
        /// ETag. Throws <see cref="Changsta.Ai.Core.Exceptions.MixLabConcurrencyException"/> on a
        /// precondition failure.
        /// </summary>
        Task<string> PutAsync(string content, string? ifMatchETag, CancellationToken cancellationToken);
    }
}
