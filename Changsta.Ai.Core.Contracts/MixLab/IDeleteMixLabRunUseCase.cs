using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Deletes a MixLab Anywhere run and purges its entry from the concept-history document, so a
    /// deleted run stops influencing future runs' novelty scoring and feedback multipliers. Only a
    /// non-active (succeeded or failed) run may be deleted; a queued or running run is rejected so
    /// the delete never races an in-flight worker claim/complete. See issue #130.
    /// </summary>
    public interface IDeleteMixLabRunUseCase
    {
        Task<DeleteMixLabRunResult> DeleteAsync(string runId, CancellationToken cancellationToken);
    }
}
