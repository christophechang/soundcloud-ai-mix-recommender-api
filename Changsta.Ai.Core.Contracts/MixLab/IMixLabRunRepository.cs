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
        /// Marks a run succeeded and stores its concepts. Idempotent: calling this again for a
        /// run that is already <see cref="MixLabRunStatus.Succeeded"/> is a no-op. Throws
        /// <see cref="Changsta.Ai.Core.Exceptions.MixLabInvalidRunStateException"/> if the run is
        /// not currently running (and not already succeeded).
        /// </summary>
        Task CompleteAsync(string runId, IReadOnlyList<MixLabRunConcept> concepts, CancellationToken cancellationToken);

        Task FailAsync(string runId, string error, CancellationToken cancellationToken);

        Task UpdateConceptFeedbackAsync(
            string runId,
            string conceptId,
            MixLabConceptFeedback feedback,
            CancellationToken cancellationToken);
    }
}
