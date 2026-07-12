using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Persistence for <c>feedback/pending.json</c> — feedback events not yet synced into the
    /// engine's concept history by the worker. See docs/architecture/mixlab-anywhere.md §3 and §4
    /// row 15.
    /// </summary>
    public interface IMixLabFeedbackQueue
    {
        Task AppendAsync(MixLabFeedbackEvent feedbackEvent, CancellationToken cancellationToken);

        Task<IReadOnlyList<MixLabFeedbackEvent>> GetPendingAsync(CancellationToken cancellationToken);

        /// <summary>Removes the given events from the pending queue after the worker has merged them into history.</summary>
        Task AckAsync(IReadOnlyList<string> eventIds, CancellationToken cancellationToken);
    }
}
