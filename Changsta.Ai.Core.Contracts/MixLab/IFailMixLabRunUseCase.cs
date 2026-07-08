using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Fails a running MixLab run, storing the error and a bounded tail of the worker log.
    /// Idempotent for an already-failed run. Backs <c>POST /api/mixlab/runs/{id}/fail</c>. See
    /// docs/architecture/mixlab-anywhere.md §4 and issue #130.
    /// </summary>
    public interface IFailMixLabRunUseCase
    {
        /// <summary>
        /// Marks the run failed with <paramref name="error"/>; <paramref name="logTail"/> (if any)
        /// is truncated to 8 KB and folded into the stored error. Only a running run may be failed.
        /// </summary>
        Task<FailMixLabRunResult> FailAsync(
            string runId,
            string error,
            string? logTail,
            CancellationToken cancellationToken);
    }
}
