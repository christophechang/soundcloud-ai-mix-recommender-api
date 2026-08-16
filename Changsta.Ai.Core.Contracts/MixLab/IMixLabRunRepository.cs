using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Persistence for MixLab Anywhere run manifests and the run archive index. See
    /// docs/architecture/mixlab-anywhere.md §3, §4, and §5.1.
    /// </summary>
    public interface IMixLabRunRepository
    {
        Task<IReadOnlyList<MixLabRunIndexEntry>> GetIndexAsync(int take, int skip, CancellationToken cancellationToken);

        Task<MixLabRun?> GetAsync(string runId, CancellationToken cancellationToken);

        Task<MixLabRun> CreateQueuedAsync(MixLabRunFlags flags, string uploadId, CancellationToken cancellationToken);

        /// <summary>
        /// Atomically claims the oldest queued run, requeuing any stale claims (running with a
        /// <c>claimedAt</c> older than <paramref name="staleLease"/>) first. Returns
        /// <see langword="null"/> when no run is queued. See docs/architecture/mixlab-anywhere.md
        /// §4 row 10.
        /// </summary>
        Task<MixLabRun?> TryClaimOldestQueuedAsync(string workerId, TimeSpan staleLease, CancellationToken cancellationToken);

        /// <summary>
        /// Whether any run is running under a live claim — claimed less than
        /// <paramref name="staleLease"/> ago, so a worker is holding it right now. A run whose
        /// claim has gone stale (or that carries no <c>claimedAt</c>) does not count: its worker
        /// is gone, and the next claim requeues it. Queued runs do not count either — nothing has
        /// read the concept history on their behalf yet. See
        /// <see cref="IDeleteMixLabRunUseCase"/> for why a delete needs to know.
        /// </summary>
        Task<bool> HasLiveRunningRunAsync(TimeSpan staleLease, CancellationToken cancellationToken);

        /// <summary>
        /// Marks a run succeeded and stores its concepts. Idempotent: calling this again for a
        /// run that is already <see cref="MixLabRunStatus.Succeeded"/> is a no-op. Throws
        /// <see cref="Changsta.Ai.Core.Exceptions.MixLabInvalidRunStateException"/> if the run is
        /// not currently running (and not already succeeded).
        /// </summary>
        Task CompleteAsync(string runId, IReadOnlyList<MixLabRunConcept> concepts, CancellationToken cancellationToken);

        Task FailAsync(string runId, string error, CancellationToken cancellationToken);

        /// <summary>
        /// Merges feedback onto a run's concept, then refreshes that run's index counts (a played or
        /// played-modified verdict is what <see cref="MixLabRunIndexEntry.PlayedCount"/> counts).
        /// Throws <see cref="Changsta.Ai.Core.Exceptions.MixLabInvalidRunStateException"/> when the
        /// run or the concept does not exist.
        /// </summary>
        Task UpdateConceptFeedbackAsync(
            string runId,
            string conceptId,
            MixLabConceptFeedback feedback,
            CancellationToken cancellationToken);

        /// <summary>
        /// Sets or clears the operator's "in session" marker on one of a run's concepts, then
        /// refreshes that run's index counts. Idempotent. Throws
        /// <see cref="Changsta.Ai.Core.Exceptions.MixLabInvalidRunStateException"/> when the run or
        /// the concept does not exist. Writes no feedback event: shortlisting is a web/API concern
        /// and never reaches engine history.
        /// </summary>
        Task UpdateConceptShortlistAsync(
            string runId,
            string conceptId,
            bool shortlisted,
            CancellationToken cancellationToken);

        /// <summary>
        /// Permanently removes a run: its manifest and every per-run artifact blob
        /// (<c>runs/{runId}/*</c>) plus its entry in the archive index. Idempotent — deleting a run
        /// that does not exist (no manifest, no index entry) is a no-op. Does not touch the concept
        /// history document; the caller purges that separately. See docs/architecture/mixlab-anywhere.md §3.
        /// </summary>
        Task DeleteAsync(string runId, CancellationToken cancellationToken);
    }
}
