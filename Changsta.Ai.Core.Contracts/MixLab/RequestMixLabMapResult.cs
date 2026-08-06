using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Outcome of <see cref="IRequestMixLabMapUseCase.RequestAsync"/>. Carries the map job on
    /// success so the controller can surface it without a re-read.
    /// </summary>
    public sealed class RequestMixLabMapResult
    {
        /// <summary>The mutually-exclusive results of a request attempt.</summary>
        public enum RequestOutcome
        {
            /// <summary>The upload resolved and a map job was requested (→ 202).</summary>
            Accepted,

            /// <summary>A concrete upload id was supplied that does not exist (→ 404).</summary>
            UnknownUpload,

            /// <summary>The request asked for <c>latest</c> but no uploads exist yet (→ 404).</summary>
            NoUploadsAvailable,
        }

        required public RequestOutcome Outcome { get; init; }

        public MixLabMapJob? Job { get; init; }
    }
}
