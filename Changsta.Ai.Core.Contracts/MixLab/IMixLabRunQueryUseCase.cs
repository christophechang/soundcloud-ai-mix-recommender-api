using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Read-side queries over the run archive index and individual run manifests. Backs
    /// <c>GET /api/mixlab/runs</c> and <c>GET /api/mixlab/runs/{id}</c>. See
    /// docs/architecture/mixlab-anywhere.md §4 and issue #130.
    /// </summary>
    public interface IMixLabRunQueryUseCase
    {
        /// <summary>
        /// Returns a page of run index entries, newest first. <paramref name="take"/> and
        /// <paramref name="skip"/> are normalised (defaults 20/0, <paramref name="take"/> capped at
        /// 100) before hitting the index.
        /// </summary>
        Task<IReadOnlyList<MixLabRunIndexEntry>> ListAsync(int? take, int? skip, CancellationToken cancellationToken);

        /// <summary>Returns the run manifest, or <see langword="null"/> when the run is unknown.</summary>
        Task<MixLabRun?> GetAsync(string runId, CancellationToken cancellationToken);
    }
}
