using System;
using Changsta.Ai.Core.Dtos;

namespace Changsta.Ai.Core.Contracts.Recommendations
{
    public interface IMixRecommendationUseCase
    {
        Task<MixRecommendationResponseDto> RecommendAsync(
            MixRecommendationRequestDto request,
            CancellationToken cancellationToken);
    }
}
