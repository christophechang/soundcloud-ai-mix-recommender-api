using System;
using System.Collections.Generic;
using Changsta.Ai.Core.Contracts.Ai;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Contracts.Recommendations;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Core.BusinessProcesses.Recommendations
{
    public sealed class MixRecommendationUseCase : IMixRecommendationUseCase
    {
        private const int MaxResultsUpperBound = 20;

        // Recommender internally caps at MixesUpperBound (100) for prompt size.
        private const int CatalogueMaxItems = 200;
        private readonly IMixAiRecommender _mixAiRecommender;

        private readonly IMixCatalogueProvider _mixCatalogueProvider;
        private readonly ILogger<MixRecommendationUseCase> _logger;

        public MixRecommendationUseCase(
            IMixCatalogueProvider mixCatalogueProvider,
            IMixAiRecommender mixAiRecommender,
            ILogger<MixRecommendationUseCase> logger)
        {
            _mixCatalogueProvider = mixCatalogueProvider ?? throw new ArgumentNullException(nameof(mixCatalogueProvider));
            _mixAiRecommender = mixAiRecommender ?? throw new ArgumentNullException(nameof(mixAiRecommender));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<MixRecommendationResponseDto> RecommendAsync(MixRecommendationRequestDto request, CancellationToken cancellationToken)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.Question))
            {
                return new MixRecommendationResponseDto
                {
                    Results = Array.Empty<MixRecommendationResultDto>(),
                    ClarifyingQuestion = "Ask me what kind of mix you want.",
                };
            }

            // Out-of-range maxResults is intentionally clamped (not rejected with a 400):
            // the response echoes the effective value back as MaxResultsApplied so callers
            // can see what was used. This keeps the public contract stable for clients that
            // omit or send a slightly off value. See issue #46.
            int maxResults = Math.Clamp(request.MaxResults, 1, MaxResultsUpperBound);

            var mixes = await _mixCatalogueProvider
                .GetLatestAsync(CatalogueMaxItems, cancellationToken)
                .ConfigureAwait(false);

            var aiResults = await _mixAiRecommender
                .RecommendAsync(request.Question, mixes, maxResults, cancellationToken)
                .ConfigureAwait(false);

            var mixById = mixes.ToDictionary(m => m.Id, StringComparer.Ordinal);

            // Defence-in-depth: the AI infrastructure validator should already restrict
            // returned MixIds to the prompt set, but skip any id that no longer maps to a
            // catalogue mix (e.g. a race with a deletion / refresh) rather than throw.
            var results = new List<MixRecommendationResultDto>(aiResults.Count);
            foreach (MixAiRecommendation r in aiResults)
            {
                if (!mixById.TryGetValue(r.MixId, out Mix? mix))
                {
                    _logger.LogWarning(
                        "Dropping AI recommendation for unknown mixId. mixId={MixId} questionLength={QuestionLength}",
                        r.MixId,
                        request.Question.Length);
                    continue;
                }

                results.Add(new MixRecommendationResultDto
                {
                    MixId = mix.Id,
                    Title = mix.Title,
                    Url = mix.Url,
                    Duration = mix.Duration,
                    ImageUrl = mix.ImageUrl,
                    Reason = r.Reason,
                    Why = r.Why,
                    Confidence = r.Confidence,
                });
            }

            return new MixRecommendationResponseDto
            {
                Results = results,
                ClarifyingQuestion = null,
                MaxResultsApplied = maxResults,
            };
        }
    }
}
