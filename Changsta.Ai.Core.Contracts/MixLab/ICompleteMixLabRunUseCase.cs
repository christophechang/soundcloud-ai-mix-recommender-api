using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Completes a running MixLab run: stores the immutable artifacts, folds the summary's concepts
    /// into the manifest, and flips the run to <c>succeeded</c>. Idempotent for an already-succeeded
    /// run. Backs <c>POST /api/mixlab/runs/{id}/complete</c>. See
    /// docs/architecture/mixlab-anywhere.md §4 rows 8/11, §8, and issue #130.
    /// </summary>
    public interface ICompleteMixLabRunUseCase
    {
        /// <summary>
        /// Stores <paramref name="summary"/>, <paramref name="report"/>, and the optional
        /// <paramref name="export"/> for the run, extracts its concepts from the summary, and marks
        /// it succeeded. The <paramref name="export"/> stream is <see langword="null"/> when the
        /// worker produced no export playlist.
        /// </summary>
        Task<CompleteMixLabRunResult> CompleteAsync(
            string runId,
            Stream summary,
            Stream report,
            Stream? export,
            CancellationToken cancellationToken);
    }
}
