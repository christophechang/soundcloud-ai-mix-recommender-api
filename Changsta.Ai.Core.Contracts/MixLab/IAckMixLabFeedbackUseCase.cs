using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Removes acknowledged entries from the pending feedback queue once the worker has folded them
    /// into engine history. Idempotent: unknown ids are ignored and an empty list is a no-op. Backs
    /// <c>POST /api/mixlab/feedback/ack</c>. See docs/architecture/mixlab-anywhere.md §4 row 15 and
    /// issue #131.
    /// </summary>
    public interface IAckMixLabFeedbackUseCase
    {
        Task AckAsync(IReadOnlyList<string> eventIds, CancellationToken cancellationToken);
    }
}
