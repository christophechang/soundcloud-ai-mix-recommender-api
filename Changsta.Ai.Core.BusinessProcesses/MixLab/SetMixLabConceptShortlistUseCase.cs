using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Resolves run and concept existence to a transport-mappable outcome, then writes the
    /// shortlist flag through <see cref="IMixLabRunRepository.UpdateConceptShortlistAsync"/> (which
    /// also refreshes the run's index counts). Deliberately takes no feedback-queue dependency: a
    /// shortlist is never a feedback event.
    /// </summary>
    public sealed class SetMixLabConceptShortlistUseCase : ISetMixLabConceptShortlistUseCase
    {
        private readonly IMixLabRunRepository _runs;

        public SetMixLabConceptShortlistUseCase(IMixLabRunRepository runs)
        {
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        }

        public async Task<SetMixLabConceptShortlistResult> SetAsync(
            string runId,
            string conceptId,
            bool shortlisted,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);
            ArgumentException.ThrowIfNullOrWhiteSpace(conceptId);

            MixLabRun? run = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false);

            if (run is null)
            {
                return new SetMixLabConceptShortlistResult
                {
                    Outcome = SetMixLabConceptShortlistResult.SetOutcome.RunNotFound,
                };
            }

            if (!run.Concepts.Any(c => string.Equals(c.ConceptId, conceptId, StringComparison.Ordinal)))
            {
                return new SetMixLabConceptShortlistResult
                {
                    Outcome = SetMixLabConceptShortlistResult.SetOutcome.ConceptNotFound,
                };
            }

            await _runs
                .UpdateConceptShortlistAsync(runId, conceptId, shortlisted, cancellationToken)
                .ConfigureAwait(false);

            return new SetMixLabConceptShortlistResult
            {
                Outcome = SetMixLabConceptShortlistResult.SetOutcome.Updated,
            };
        }
    }
}
