using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Sets or clears the operator's "in session" marker on a completed run's concept, via
    /// <see cref="IMixLabRunRepository.UpdateConceptShortlistAsync"/>. Backs
    /// <c>PUT /api/mixlab/runs/{id}/concepts/{conceptId}/shortlist</c>. Unlike
    /// <see cref="ISubmitMixLabConceptFeedbackUseCase"/> it queues no feedback event — shortlisting
    /// is a web/API concern and must never reach engine history — which is why it is a separate use
    /// case rather than another field on the feedback payload.
    /// </summary>
    public interface ISetMixLabConceptShortlistUseCase
    {
        Task<SetMixLabConceptShortlistResult> SetAsync(
            string runId,
            string conceptId,
            bool shortlisted,
            CancellationToken cancellationToken);
    }
}
