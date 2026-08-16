namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Outcome of <see cref="ISetMixLabConceptShortlistUseCase.SetAsync"/>, mapped by the controller
    /// to a transport status code.
    /// </summary>
    public sealed class SetMixLabConceptShortlistResult
    {
        /// <summary>The mutually-exclusive results of a shortlist write.</summary>
        public enum SetOutcome
        {
            /// <summary>The concept now carries the requested flag (→ 204). Idempotent.</summary>
            Updated,

            /// <summary>No run exists with the supplied id (→ 404).</summary>
            RunNotFound,

            /// <summary>The run exists but has no concept with the supplied id (→ 404).</summary>
            ConceptNotFound,
        }

        required public SetOutcome Outcome { get; init; }
    }
}
