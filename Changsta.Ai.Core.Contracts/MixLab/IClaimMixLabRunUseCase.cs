using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Claims the oldest queued run for a worker, first requeuing any run whose claim lease has
    /// gone stale. Backs <c>POST /api/mixlab/worker/claim</c>. The stale-lease window is a
    /// configured option, not a caller input. See docs/architecture/mixlab-anywhere.md §4 row 10
    /// and issue #130.
    /// </summary>
    public interface IClaimMixLabRunUseCase
    {
        /// <summary>
        /// Atomically claims the oldest queued run for <paramref name="workerId"/>, or returns
        /// <see langword="null"/> when nothing is claimable (the controller then replies 204).
        /// </summary>
        Task<MixLabRun?> ClaimAsync(string workerId, CancellationToken cancellationToken);
    }
}
