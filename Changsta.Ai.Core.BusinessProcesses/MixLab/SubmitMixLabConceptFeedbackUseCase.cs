using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Domain.MixLab;
using Changsta.Ai.Core.Normalization;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Validates a concept feedback payload, merges it onto the run manifest via
    /// <see cref="IMixLabRunRepository.UpdateConceptFeedbackAsync"/>, then appends a
    /// <see cref="MixLabFeedbackEvent"/> to the pending queue for the worker to fold into engine
    /// history. A crash between the two writes is acceptable and self-healing: the worker re-derives
    /// pending state from the queue on its next sync. See docs/architecture/mixlab-anywhere.md §4
    /// row 14, §5.3, and issue #131.
    /// </summary>
    public sealed class SubmitMixLabConceptFeedbackUseCase : ISubmitMixLabConceptFeedbackUseCase
    {
        private const int MinRating = 1;

        private const int MaxRating = 5;

        private const int MaxNotesLength = 2000;

        private readonly IMixLabRunRepository _runs;
        private readonly IMixLabFeedbackQueue _feedbackQueue;
        private readonly IMixCatalogueProvider _catalogue;
        private readonly TimeProvider _timeProvider;

        public SubmitMixLabConceptFeedbackUseCase(
            IMixLabRunRepository runs,
            IMixLabFeedbackQueue feedbackQueue,
            IMixCatalogueProvider catalogue,
            TimeProvider timeProvider)
        {
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
            _feedbackQueue = feedbackQueue ?? throw new ArgumentNullException(nameof(feedbackQueue));
            _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        public async Task<SubmitMixLabConceptFeedbackResult> SubmitAsync(
            string runId,
            string conceptId,
            string? verdict,
            int? rating,
            string? notes,
            string? publishedMixSlug,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);
            ArgumentException.ThrowIfNullOrWhiteSpace(conceptId);

            if (!TryValidate(verdict, rating, notes, publishedMixSlug, out MixLabFeedbackVerdict? parsedVerdict, out string? validationError))
            {
                return Invalid(validationError!);
            }

            if (publishedMixSlug is not null
                && !await SlugExistsAsync(publishedMixSlug, cancellationToken).ConfigureAwait(false))
            {
                return Invalid($"'publishedMixSlug' value '{publishedMixSlug}' does not exist in the mix catalogue.");
            }

            MixLabRun? run = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false);
            if (run is null)
            {
                return new SubmitMixLabConceptFeedbackResult
                {
                    Outcome = SubmitMixLabConceptFeedbackResult.SubmitOutcome.RunNotFound,
                };
            }

            if (!run.Concepts.Any(c => string.Equals(c.ConceptId, conceptId, StringComparison.Ordinal)))
            {
                return new SubmitMixLabConceptFeedbackResult
                {
                    Outcome = SubmitMixLabConceptFeedbackResult.SubmitOutcome.ConceptNotFound,
                };
            }

            DateTimeOffset recordedAt = _timeProvider.GetUtcNow();

            var feedback = new MixLabConceptFeedback
            {
                Verdict = parsedVerdict,
                Rating = rating,
                Notes = notes,
                PublishedMixSlug = publishedMixSlug,
                RecordedAt = recordedAt,
            };

            await _runs.UpdateConceptFeedbackAsync(runId, conceptId, feedback, cancellationToken).ConfigureAwait(false);

            var feedbackEvent = new MixLabFeedbackEvent
            {
                EventId = Guid.NewGuid().ToString("N"),
                RunId = runId,
                ConceptId = conceptId,
                Verdict = parsedVerdict,
                Rating = rating,
                Notes = notes,
                PublishedMixSlug = publishedMixSlug,
                RecordedAt = recordedAt,
            };

            await _feedbackQueue.AppendAsync(feedbackEvent, cancellationToken).ConfigureAwait(false);

            return new SubmitMixLabConceptFeedbackResult { Outcome = SubmitMixLabConceptFeedbackResult.SubmitOutcome.Recorded };
        }

        private static SubmitMixLabConceptFeedbackResult Invalid(string message)
        {
            return new SubmitMixLabConceptFeedbackResult
            {
                Outcome = SubmitMixLabConceptFeedbackResult.SubmitOutcome.InvalidRequest,
                ErrorMessage = message,
            };
        }

        private static bool TryValidate(
            string? verdict,
            int? rating,
            string? notes,
            string? publishedMixSlug,
            out MixLabFeedbackVerdict? parsedVerdict,
            out string? error)
        {
            parsedVerdict = null;
            error = null;

            if (verdict is null && rating is null && notes is null && publishedMixSlug is null)
            {
                error = "At least one of 'verdict', 'rating', 'notes', or 'publishedMixSlug' is required.";
                return false;
            }

            if (verdict is not null)
            {
                if (!MixLabFeedbackVerdictWireValues.TryParse(verdict, out MixLabFeedbackVerdict value))
                {
                    error = "'verdict' must be one of: " +
                        $"{MixLabFeedbackVerdictWireValues.Played}, {MixLabFeedbackVerdictWireValues.PlayedModified}, " +
                        $"{MixLabFeedbackVerdictWireValues.Rejected}, {MixLabFeedbackVerdictWireValues.Unused}.";
                    return false;
                }

                parsedVerdict = value;
            }

            if (rating is int ratingValue && (ratingValue < MinRating || ratingValue > MaxRating))
            {
                error = $"'rating' must be between {MinRating} and {MaxRating}.";
                return false;
            }

            if (notes is not null && notes.Length > MaxNotesLength)
            {
                error = $"'notes' must be at most {MaxNotesLength} characters.";
                return false;
            }

            return true;
        }

        private async Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken)
        {
            IReadOnlyList<Mix> mixes = await _catalogue.GetLatestAsync(int.MaxValue, cancellationToken).ConfigureAwait(false);
            return mixes.Any(m => string.Equals(MixSlugHelper.ExtractSlug(m.Url), slug, StringComparison.OrdinalIgnoreCase));
        }
    }
}
