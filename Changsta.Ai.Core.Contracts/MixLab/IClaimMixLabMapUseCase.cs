using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Claims the oldest queued map job for a worker, first requeuing any job whose claim lease has
    /// gone stale. Backs <c>POST /api/mixlab/maps/claim</c>. The stale-lease window is a configured
    /// option, not a caller input.
    /// </summary>
    public interface IClaimMixLabMapUseCase
    {
        /// <summary>
        /// Atomically claims the oldest queued map job for <paramref name="workerId"/>, or returns
        /// <see langword="null"/> when nothing is claimable (the controller then replies 204).
        /// </summary>
        Task<MixLabMapJob?> ClaimAsync(string workerId, CancellationToken cancellationToken);
    }
}
