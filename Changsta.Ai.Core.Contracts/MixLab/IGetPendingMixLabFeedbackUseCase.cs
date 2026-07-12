using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Returns the pending feedback queue verbatim from <see cref="IMixLabFeedbackQueue"/>. Backs
    /// <c>GET /api/mixlab/feedback/pending</c>. See docs/architecture/mixlab-anywhere.md §4 row 15
    /// and issue #131.
    /// </summary>
    public interface IGetPendingMixLabFeedbackUseCase
    {
        Task<IReadOnlyList<MixLabFeedbackEvent>> GetPendingAsync(CancellationToken cancellationToken);
    }
}
