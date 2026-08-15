using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Deletes a MixLab Anywhere run and purges its entry from the concept-history document, so a
    /// deleted run stops influencing future runs' novelty scoring and feedback multipliers. Only a
    /// non-active (succeeded or failed) run may be deleted; a queued or running run is rejected so
    /// the delete never races an in-flight worker claim/complete. See issue #130.
    /// <para>
    /// A delete is also rejected while <em>any</em> run holds a live claim. The engine's worker
    /// adopts the history document at the start of a run and pushes its own copy back at the end,
    /// merging on conflict by re-adding run ids the remote no longer has — which would silently
    /// resurrect the history entry this use case just purged. Waiting out the in-flight run is the
    /// whole guard: with no worker mid-run, nothing holds a pre-purge snapshot.
    /// </para>
    /// </summary>
    public interface IDeleteMixLabRunUseCase
    {
        Task<DeleteMixLabRunResult> DeleteAsync(string runId, CancellationToken cancellationToken);
    }
}
