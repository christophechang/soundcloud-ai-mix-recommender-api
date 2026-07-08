namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Outcome of <see cref="ICompleteMixLabRunUseCase.CompleteAsync"/>, mapped by the controller
    /// to a transport status code.
    /// </summary>
    public sealed class CompleteMixLabRunResult
    {
        /// <summary>The mutually-exclusive results of a complete attempt.</summary>
        public enum CompleteOutcome
        {
            /// <summary>Artifacts stored and the run transitioned to succeeded (→ 200).</summary>
            Completed,

            /// <summary>The run was already succeeded; a no-op that does not rewrite artifacts (→ 200).</summary>
            AlreadyCompleted,

            /// <summary>The run exists but is not running (and not already succeeded) (→ 409).</summary>
            Conflict,

            /// <summary>No run exists with the supplied id (→ 404).</summary>
            NotFound,

            /// <summary>The summary payload was not valid JSON (→ 400).</summary>
            InvalidSummary,
        }

        required public CompleteOutcome Outcome { get; init; }

        public string? ErrorMessage { get; init; }
    }
}
