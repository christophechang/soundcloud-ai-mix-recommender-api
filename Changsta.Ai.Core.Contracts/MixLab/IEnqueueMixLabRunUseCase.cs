using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Enqueues a MixLab Anywhere run: validates the CLI flags against a strict allow-list,
    /// resolves the target upload (a concrete id or the literal <c>latest</c>) to a concrete id at
    /// enqueue time so the run is reproducible, and writes a <c>queued</c> run manifest. Backs
    /// <c>POST /api/mixlab/runs</c>. See docs/architecture/mixlab-anywhere.md §4 and issue #130.
    /// </summary>
    public interface IEnqueueMixLabRunUseCase
    {
        /// <summary>
        /// Validates <paramref name="flags"/> and <paramref name="uploadId"/> and, when valid,
        /// creates a queued run. The caller maps <see cref="EnqueueMixLabRunResult.Outcome"/> to a
        /// transport status code.
        /// </summary>
        Task<EnqueueMixLabRunResult> EnqueueAsync(JsonElement flags, string? uploadId, CancellationToken cancellationToken);
    }
}
