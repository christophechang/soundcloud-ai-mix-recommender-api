namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Outcome of <see cref="ISubmitMixLabConceptFeedbackUseCase.SubmitAsync"/>, mapped by the
    /// controller to a transport status code.
    /// </summary>
    public sealed class SubmitMixLabConceptFeedbackResult
    {
        /// <summary>The mutually-exclusive results of a feedback submission.</summary>
        public enum SubmitOutcome
        {
            /// <summary>Feedback was merged onto the run and queued for the worker (→ 200).</summary>
            Recorded,

            /// <summary>
            /// The payload failed validation: an unrecognised verdict, an out-of-range rating,
            /// notes over length, no field supplied, or an unknown <c>publishedMixSlug</c> (→ 400).
            /// </summary>
            InvalidRequest,

            /// <summary>No run exists with the supplied id (→ 404).</summary>
            RunNotFound,

            /// <summary>The run exists but has no concept with the supplied id (→ 404).</summary>
            ConceptNotFound,
        }

        required public SubmitOutcome Outcome { get; init; }

        public string? ErrorMessage { get; init; }
    }
}
