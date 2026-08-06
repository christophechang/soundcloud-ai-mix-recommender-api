using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Outcome of <see cref="IGetMixLabMapUseCase.GetAsync"/>.
    /// </summary>
    public sealed class GetMixLabMapResult
    {
        /// <summary>The mutually-exclusive results of a get attempt.</summary>
        public enum GetOutcome
        {
            /// <summary>A map job exists for the resolved upload (→ 200).</summary>
            Found,

            /// <summary>No map job exists for the resolved upload (→ 404).</summary>
            NotFound,
        }

        required public GetOutcome Outcome { get; init; }

        public MixLabMapJob? Job { get; init; }

        /// <summary>
        /// Raw engine payload bytes when <see cref="MixLabMapJob.Status"/> is
        /// <see cref="MixLabMapStatus.Succeeded"/>; <see langword="null"/> otherwise.
        /// </summary>
        public byte[]? Payload { get; init; }
    }
}
