using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Recomputes the archive index's derived counts from the run manifests, via
    /// <see cref="IMixLabRunRepository.RecomputeIndexCountsAsync"/>. Backs
    /// <c>POST /api/mixlab/runs/reindex</c>: the one-shot backfill after a deploy that introduces
    /// the counts, and the repair for drift. Returns the number of runs recomputed.
    /// </summary>
    public interface IReindexMixLabRunsUseCase
    {
        Task<int> ReindexAsync(CancellationToken cancellationToken);
    }
}
