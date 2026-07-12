using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Removes acknowledged entries from the pending feedback queue via
    /// <see cref="IMixLabFeedbackQueue.AckAsync"/>, which is itself idempotent (unknown ids are
    /// ignored, an empty list is a no-op). See docs/architecture/mixlab-anywhere.md §4 row 15 and
    /// issue #131.
    /// </summary>
    public sealed class AckMixLabFeedbackUseCase : IAckMixLabFeedbackUseCase
    {
        private readonly IMixLabFeedbackQueue _feedbackQueue;

        public AckMixLabFeedbackUseCase(IMixLabFeedbackQueue feedbackQueue)
        {
            _feedbackQueue = feedbackQueue ?? throw new ArgumentNullException(nameof(feedbackQueue));
        }

        public Task AckAsync(IReadOnlyList<string> eventIds, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(eventIds);

            return _feedbackQueue.AckAsync(eventIds, cancellationToken);
        }
    }
}
