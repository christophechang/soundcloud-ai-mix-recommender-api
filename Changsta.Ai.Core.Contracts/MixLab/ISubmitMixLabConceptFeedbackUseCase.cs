using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Records field feedback for a completed run's concept: validates the payload, merges it onto
    /// the run manifest's concept via <see cref="IMixLabRunRepository.UpdateConceptFeedbackAsync"/>,
    /// then appends a <c>MixLabFeedbackEvent</c> to the pending queue for the worker to fold into
    /// engine history. Backs <c>POST /api/mixlab/runs/{id}/concepts/{conceptId}/feedback</c>. See
    /// docs/architecture/mixlab-anywhere.md §4 row 14, §5.3, and issue #131.
    /// </summary>
    public interface ISubmitMixLabConceptFeedbackUseCase
    {
        /// <summary>
        /// <paramref name="verdict"/> is the raw wire string (see
        /// <see cref="MixLabFeedbackVerdictWireValues"/>), not yet parsed — validation and parsing
        /// both happen here so a bad value is reported once, uniformly. Every parameter besides
        /// <paramref name="runId"/> and <paramref name="conceptId"/> is optional, but at least one
        /// must be supplied.
        /// </summary>
        Task<SubmitMixLabConceptFeedbackResult> SubmitAsync(
            string runId,
            string conceptId,
            string? verdict,
            int? rating,
            string? notes,
            string? publishedMixSlug,
            CancellationToken cancellationToken);
    }
}
