using System;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Changsta.Ai.Interface.Api.Controllers
{
    [ApiController]
    [Route("api/diagnostics")]
    [Produces("application/json")]
    public sealed class DiagnosticsController : ControllerBase
    {
        private const int DefaultWindowHours = 24;
        private const int MaxWindowHours = 168;

        private readonly IGetErrorInsightsUseCase _getErrorInsightsUseCase;
        private readonly IConfiguration _configuration;

        public DiagnosticsController(
            IGetErrorInsightsUseCase getErrorInsightsUseCase,
            IConfiguration configuration)
        {
            _getErrorInsightsUseCase = getErrorInsightsUseCase;
            _configuration = configuration;
        }

        [HttpGet("errors")]
        public async Task<IActionResult> GetErrorsAsync(
            [FromQuery] int hours = DefaultWindowHours,
            CancellationToken cancellationToken = default)
        {
            string? expectedSecret = _configuration["Catalog:FlushSecret"];
            if (!string.IsNullOrEmpty(expectedSecret))
            {
                if (!Request.Headers.TryGetValue("Authorization", out var authHeader)
                    || !string.Equals(authHeader.ToString(), $"Bearer {expectedSecret}", StringComparison.Ordinal))
                {
                    return Unauthorized(new { error = "Invalid or missing authorization." });
                }
            }

            if (hours < 1 || hours > MaxWindowHours)
            {
                return BadRequest(new { error = $"hours must be between 1 and {MaxWindowHours}." });
            }

            var result = await _getErrorInsightsUseCase
                .GetErrorsAsync(hours, cancellationToken)
                .ConfigureAwait(false);

            return Ok(result);
        }
    }
}
