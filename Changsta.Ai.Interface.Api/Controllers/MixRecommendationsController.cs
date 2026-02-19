using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Recommendations;
using Changsta.Ai.Core.Dtos;
using Microsoft.AspNetCore.Mvc;

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