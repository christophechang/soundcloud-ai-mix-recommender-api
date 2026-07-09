using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Returns the raw <c>history/concept-history.json</c> document verbatim from
    /// <see cref="IMixLabHistoryStore"/>. Backs <c>GET /api/mixlab/history</c>. See
    /// docs/architecture/mixlab-anywhere.md §4 row 13 and issue #131.
    /// </summary>
    public interface IGetMixLabHistoryUseCase
    {
        /// <summary>Returns <see langword="null"/> when the history document does not exist yet.</summary>
        Task<MixLabHistorySnapshot?> GetAsync(CancellationToken cancellationToken);
    }
}
