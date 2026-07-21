using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Diagnostics;
using Changsta.Ai.Interface.Api.Errors;
using Changsta.Ai.Interface.Api.RateLimiting;
using Changsta.Ai.Interface.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Changsta.Ai.Interface.Api.Controllers
{
    [ApiController]
    [Route("api/diagnostics")]
    public sealed class DiagnosticsController : ControllerBase
    {
        private const int DefaultWindowHours = 24;
        private const int MaxWindowHours = 168;

        private readonly IGetErrorInsightsUseCase _getErrorInsightsUseCase;

        public DiagnosticsController(IGetErrorInsightsUseCase getErrorInsightsUseCase)
        {
            _getErrorInsightsUseCase = getErrorInsightsUseCase;
        }

        [HttpGet("errors")]
        [EnableRateLimiting(RateLimitPolicies.Privileged)]
        [BearerSecret("Catalog:FlushSecret")]
        public async Task<IActionResult> GetErrorsAsync(
            [FromQuery] int hours = DefaultWindowHours,
            CancellationToken cancellationToken = default)
        {
            if (hours < 1 || hours > MaxWindowHours)
            {
                return ApiProblem.BadRequest($"hours must be between 1 and {MaxWindowHours}.");
            }

            var result = await _getErrorInsightsUseCase
                .GetErrorsAsync(hours, cancellationToken)
                .ConfigureAwait(false);

            return Ok(result);
        }
    }
}
