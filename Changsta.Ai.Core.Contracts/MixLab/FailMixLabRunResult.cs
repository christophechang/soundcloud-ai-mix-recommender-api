namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Outcome of <see cref="IFailMixLabRunUseCase.FailAsync"/>, mapped by the controller to a
    /// transport status code.
    /// </summary>
    public sealed class FailMixLabRunResult
    {
        /// <summary>The mutually-exclusive results of a fail attempt.</summary>
        public enum FailOutcome
        {
            /// <summary>The running run transitioned to failed (→ 200).</summary>
            Failed,

            /// <summary>The run was already failed; a no-op (→ 200).</summary>
            AlreadyFailed,

            /// <summary>The run exists but is not running (queued or succeeded) (→ 409).</summary>
            Conflict,

            /// <summary>No run exists with the supplied id (→ 404).</summary>
            NotFound,
        }

        required public FailOutcome Outcome { get; init; }
    }
}
