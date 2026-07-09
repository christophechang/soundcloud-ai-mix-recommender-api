using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Returns the pending feedback queue verbatim from <see cref="IMixLabFeedbackQueue"/>. See
    /// docs/architecture/mixlab-anywhere.md §4 row 15 and issue #131.
    /// </summary>
    public sealed class GetPendingMixLabFeedbackUseCase : IGetPendingMixLabFeedbackUseCase
    {
        private readonly IMixLabFeedbackQueue _feedbackQueue;

        public GetPendingMixLabFeedbackUseCase(IMixLabFeedbackQueue feedbackQueue)
        {
            _feedbackQueue = feedbackQueue ?? throw new ArgumentNullException(nameof(feedbackQueue));
        }

        public Task<IReadOnlyList<MixLabFeedbackEvent>> GetPendingAsync(CancellationToken cancellationToken) =>
            _feedbackQueue.GetPendingAsync(cancellationToken);
    }
}
