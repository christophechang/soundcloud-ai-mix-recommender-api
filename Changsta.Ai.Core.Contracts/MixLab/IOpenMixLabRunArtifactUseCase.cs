using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Opens a run artifact (<c>report.html</c>, <c>export.xml</c>, or <c>summary.json</c>) for
    /// streaming download. Backs <c>GET /api/mixlab/runs/{id}/report|export|summary</c>. See
    /// docs/architecture/mixlab-anywhere.md §4 and issue #130.
    /// </summary>
    public interface IOpenMixLabRunArtifactUseCase
    {
        /// <summary>
        /// Resolves the requested artifact for streaming. Distinguishes an unknown run from a
        /// missing artifact so the controller can 404 with the right reason and otherwise stream
        /// the content with the correct content type.
        /// </summary>
        Task<MixLabRunArtifactResult> OpenAsync(string runId, MixLabRunArtifactKind kind, CancellationToken cancellationToken);
    }
}
