namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Outcome of <see cref="IEnqueueMixLabRunUseCase.EnqueueAsync"/>. Carries the new run id on
    /// success or a human-readable, key-naming error message on rejection so the controller can
    /// surface a precise 400/404 without re-deriving the reason.
    /// </summary>
    public sealed class EnqueueMixLabRunResult
    {
        /// <summary>The mutually-exclusive results of an enqueue attempt.</summary>
        public enum EnqueueOutcome
        {
            /// <summary>Flags and upload validated; a queued run manifest was written (→ 202).</summary>
            Created,

            /// <summary>Flags failed allow-list validation or the upload reference was malformed (→ 400).</summary>
            InvalidRequest,

            /// <summary>The request asked for <c>latest</c> but no uploads exist yet (→ 400).</summary>
            NoUploadsAvailable,

            /// <summary>A concrete upload id was supplied that does not exist (→ 404).</summary>
            UnknownUpload,
        }

        required public EnqueueOutcome Outcome { get; init; }

        public string? RunId { get; init; }

        public string? ErrorMessage { get; init; }
    }
}
