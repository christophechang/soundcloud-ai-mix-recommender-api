using System;
using System.Collections.Generic;
using Changsta.Ai.Core.Contracts.Ai;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Contracts.Recommendations;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;

namespace Changsta.Ai.Core.BusinessProcesses.Recommendations
{
    public sealed class MixRecommendationUseCase : IMixRecommendationUseCase
    {
        private const int MaxResultsUpperBound = 20;

        // Recommender internally caps at MixesUpperBound (100) for prompt size.
        private const int CatalogueMaxItems = 200;
        private readonly IMixAiRecommender _mixAiRecommender;

        private readonly IMixCatalogueProvider _mixCatalogueProvider;

        public MixRecommendationUseCase(
            IMixCatalogueProvider mixCatalogueProvider,
            IMixAiRecommender mixAiRecommender)
        {
            _mixCatalogueProvider = mixCatalogueProvider ?? throw new ArgumentNullException(nameof(mixCatalogueProvider));
            _mixAiRecommender = mixAiRecommender ?? throw new ArgumentNullException(nameof(mixAiRecommender));
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

            int maxResults = Math.Clamp(request.MaxResults, 1, MaxResultsUpperBound);

            var mixes = await _mixCatalogueProvider
                .GetLatestAsync(CatalogueMaxItems, cancellationToken)
                .ConfigureAwait(false);

            var aiResults = await _mixAiRecommender
                .RecommendAsync(request.Question, mixes, maxResults, cancellationToken)
                .ConfigureAwait(false);

            var mixById = mixes.ToDictionary(m => m.Id, StringComparer.Ordinal);

            var results = aiResults
                .Select(r =>
                {
                    Mix mix = mixById[r.MixId];

                    return new MixRecommendationResultDto
                    {
                        MixId = mix.Id,
                        Title = mix.Title,
                        Url = mix.Url,
                        Duration = mix.Duration,
                        ImageUrl = mix.ImageUrl,
                        Reason = r.Reason,
                        Why = r.Why,
                        Confidence = r.Confidence,
                    };
                })
                .ToArray();

            return new MixRecommendationResponseDto
            {
                Results = results,
                ClarifyingQuestion = null,
                MaxResultsApplied = maxResults,
            };
        }
    }
}
