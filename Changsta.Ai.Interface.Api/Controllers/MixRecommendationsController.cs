using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Recommendations;
using Changsta.Ai.Core.Dtos;
using Changsta.Ai.Interface.Api.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Changsta.Ai.Interface.Api.Controllers
{
    [ApiController]
    [Route("api/mixes")]
    public sealed class MixRecommendationsController : ControllerBase
    {
        private readonly IMixRecommendationUseCase _mixRecommendationUseCase;

        public MixRecommendationsController(IMixRecommendationUseCase mixRecommendationUseCase)
        {
            _mixRecommendationUseCase = mixRecommendationUseCase;
        }

        [HttpPost("recommend")]
        [EnableRateLimiting(RateLimitPolicies.Recommend)]
        public async Task<ActionResult<MixRecommendationResponseDto>> Recommend(
            [FromBody] MixRecommendationRequestDto request,
            CancellationToken cancellationToken)
        {
            MixRecommendationResponseDto response =
                await _mixRecommendationUseCase
                    .RecommendAsync(request, cancellationToken)
                    .ConfigureAwait(false);

            return Ok(response);
        }
    }
}
