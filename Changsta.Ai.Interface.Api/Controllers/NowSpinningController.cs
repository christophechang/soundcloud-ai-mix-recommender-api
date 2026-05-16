using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.NowSpinning;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;
using Changsta.Ai.Interface.Api.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Changsta.Ai.Interface.Api.Controllers
{
    [ApiController]
    [Route("api/catalog")]
    [Produces("application/json")]
    public sealed class NowSpinningController : ControllerBase
    {
        private readonly INowSpinningUseCase _useCase;

        public NowSpinningController(INowSpinningUseCase useCase)
        {
            _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        }

        [HttpGet("now-spinning")]
        public async Task<IActionResult> GetNowSpinningAsync(
            [FromQuery] int utcOffsetMinutes = 0,
            [FromQuery] string? moodLean = null,
            [FromQuery] string? skip = null,
            [FromQuery] int schedule = 4,
            CancellationToken cancellationToken = default)
        {
            MoodLean? parsedLean = null;

            if (!string.IsNullOrEmpty(moodLean))
            {
                if (!TryParseMoodLean(moodLean, out MoodLean lean))
                {
                    return BadRequest(new { error = "invalid moodLean" });
                }

                parsedLean = lean;
            }

            string[] skipIds = string.IsNullOrWhiteSpace(skip)
                ? Array.Empty<string>()
                : skip.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var request = new NowSpinningRequestDto
            {
                UtcNow = DateTimeOffset.UtcNow,
                UtcOffsetMinutes = utcOffsetMinutes,
                MoodLean = parsedLean,
                SkipIds = skipIds,
                ScheduleCount = Math.Max(0, schedule),
            };

            NowSpinningResultDto result = await _useCase
                .GetAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (result.NoMixAvailable)
            {
                return StatusCode(503, new { error = "no mixes available" });
            }

            return Ok(MapToResponse(result));
        }

        private static bool TryParseMoodLean(string value, out MoodLean lean)
        {
            switch (value.ToLowerInvariant())
            {
                case "darker": lean = MoodLean.Darker; return true;
                case "warmer": lean = MoodLean.Warmer; return true;
                case "slower": lean = MoodLean.Slower; return true;
                case "faster": lean = MoodLean.Faster; return true;
                default: lean = default; return false;
            }
        }

        private static NowSpinningResponse MapToResponse(NowSpinningResultDto result)
        {
            return new NowSpinningResponse
            {
                Now = result.Now,
                DayBucket = result.DayBucket,
                Slot = new NowSpinningSlotVm { Key = result.Slot.Key, Label = result.Slot.Label },
                Mix = result.Mix is not null ? MapMix(result.Mix) : null,
                Schedule = result.Schedule
                    .Select(s => new NowSpinningScheduleEntryVm
                    {
                        At = s.At,
                        Slot = new NowSpinningSlotVm { Key = s.Slot.Key, Label = s.Slot.Label },
                        DayBucket = s.DayBucket,
                        Mix = s.Mix is not null ? MapMix(s.Mix) : null,
                    })
                    .ToArray(),
                LeanIgnored = result.LeanIgnored,
                SkipsIgnored = result.SkipsIgnored,
                PoolFallback = result.PoolFallback,
            };
        }

        private static NowSpinningMixVm MapMix(Mix mix)
        {
            return new NowSpinningMixVm
            {
                Id = mix.Id,
                Title = mix.Title,
                Url = mix.Url,
                Genre = mix.Genre,
                Energy = mix.Energy,
                Bpm = ComputeBpm(mix),
                Moods = mix.Moods,
                PublishedAt = mix.PublishedAt,
                Duration = ParseDurationSeconds(mix.Duration),
            };
        }

        private static int? ComputeBpm(Mix mix)
        {
            if (mix.BpmMin.HasValue && mix.BpmMax.HasValue)
            {
                return (int)Math.Round((mix.BpmMin.Value + mix.BpmMax.Value) / 2.0);
            }

            return mix.BpmMin ?? mix.BpmMax;
        }

        private static int? ParseDurationSeconds(string? duration)
        {
            if (duration is null)
            {
                return null;
            }

            if (TimeSpan.TryParse(duration, out TimeSpan ts))
            {
                return (int)ts.TotalSeconds;
            }

            return null;
        }
    }
}
