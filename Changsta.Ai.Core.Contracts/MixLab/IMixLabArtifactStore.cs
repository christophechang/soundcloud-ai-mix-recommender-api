using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Persistence for immutable per-run artifacts (<c>report.html</c>, <c>export.xml</c>,
    /// <c>summary.json</c>) — written once and never mutated. See
    /// docs/architecture/mixlab-anywhere.md §3.
    /// </summary>
    public interface IMixLabArtifactStore
    {
        Task SaveAsync(string runId, string name, Stream content, CancellationToken cancellationToken);

        Task<Stream> OpenReadAsync(string runId, string name, CancellationToken cancellationToken);
    }
}
