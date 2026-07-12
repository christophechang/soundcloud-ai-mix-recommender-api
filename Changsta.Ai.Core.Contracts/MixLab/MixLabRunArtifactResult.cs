using System.IO;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Outcome of <see cref="IOpenMixLabRunArtifactUseCase.OpenAsync"/>. On
    /// <see cref="ArtifactStatus.Found"/> the caller owns and must dispose <see cref="Content"/>.
    /// </summary>
    public sealed class MixLabRunArtifactResult
    {
        /// <summary>The mutually-exclusive results of an artifact open.</summary>
        public enum ArtifactStatus
        {
            /// <summary>The artifact was opened; stream and content type are populated (→ 200).</summary>
            Found,

            /// <summary>No run exists with the supplied id (→ 404).</summary>
            RunNotFound,

            /// <summary>The run exists but the requested artifact does not (→ 404).</summary>
            ArtifactNotFound,
        }

        required public ArtifactStatus Status { get; init; }

        public Stream? Content { get; init; }

        public string? ContentType { get; init; }
    }
}
