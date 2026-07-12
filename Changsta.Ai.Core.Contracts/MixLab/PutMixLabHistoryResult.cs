namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Outcome of <see cref="IPutMixLabHistoryUseCase.PutAsync"/>, mapped by the controller to a
    /// transport status code.
    /// </summary>
    public sealed class PutMixLabHistoryResult
    {
        /// <summary>The mutually-exclusive results of a history write attempt.</summary>
        public enum PutOutcome
        {
            /// <summary>The document was written (→ 200 with the new ETag).</summary>
            Written,

            /// <summary>The request body was not well-formed JSON (→ 400).</summary>
            InvalidJson,

            /// <summary>
            /// The supplied <c>If-Match</c> did not match the current ETag, or was absent while a
            /// document already exists (→ 412).
            /// </summary>
            PreconditionFailed,
        }

        required public PutOutcome Outcome { get; init; }

        public string? ETag { get; init; }

        public string? ErrorMessage { get; init; }
    }
}
