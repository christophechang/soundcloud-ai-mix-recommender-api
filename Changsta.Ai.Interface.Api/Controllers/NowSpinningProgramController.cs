using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.NowSpinning;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;
using Changsta.Ai.Interface.Api.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Changsta.Ai.Interface.Api.Controllers
{
    [ApiController]
    [Route("api/catalog")]
    [Produces("application/json")]
    public sealed class NowSpinningProgramController : ControllerBase
    {
        private readonly INowSpinningProgramUseCase _useCase;
        private readonly IMemoryCache _cache;

        public NowSpinningProgramController(INowSpinningProgramUseCase useCase, IMemoryCache cache)
        {
            _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        [HttpGet("now-spinning/program")]
        public async Task<IActionResult> GetProgramAsync(
            [FromQuery] int utcOffsetMinutes = 0,
            [FromQuery] string? skip = null,
            [FromQuery] int schedule = 4,
            CancellationToken cancellationToken = default)
        {
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;

            long hourEpochMs = new DateTimeOffset(
                utcNow.Year,
                utcNow.Month,
                utcNow.Day,
                utcNow.Hour,
                0,
                0,
                TimeSpan.Zero)
                .ToUnixTimeMilliseconds();

            string cacheKey = $"now-spinning-program:{hourEpochMs}:{utcOffsetMinutes}";

            if (_cache.TryGetValue(cacheKey, out NowSpinningProgramResponse? cached))
            {
                return Ok(cached);
            }

            string[] skipIds = string.IsNullOrWhiteSpace(skip)
                ? Array.Empty<string>()
                : skip.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var request = new NowSpinningProgramRequestDto
            {
                UtcNow = utcNow,
                UtcOffsetMinutes = utcOffsetMinutes,
                SkipIds = skipIds,
                ScheduleCount = Math.Max(0, schedule),
            };

            NowSpinningProgramResultDto result = await _useCase
                .GetAsync(request, cancellationToken)
                .ConfigureAwait(false);

            NowSpinningProgramResponse response = MapToResponse(result);

            DateTimeOffset nextHour = new DateTimeOffset(
                utcNow.Year,
                utcNow.Month,
                utcNow.Day,
                utcNow.Hour,
                0,
                0,
                TimeSpan.Zero)
                .AddHours(1);

            _cache.Set(
                cacheKey,
                response,
                new MemoryCacheEntryOptions { AbsoluteExpiration = nextHour });

            return Ok(response);
        }

        private static NowSpinningProgramResponse MapToResponse(NowSpinningProgramResultDto result)
        {
            var lanes = new Dictionary<string, NowSpinningProgramLaneVm>(result.Lanes.Count);

            foreach (KeyValuePair<string, NowSpinningProgramLaneDto> kvp in result.Lanes)
            {
                lanes[kvp.Key] = MapLane(kvp.Value);
            }

            return new NowSpinningProgramResponse
            {
                Now = result.Now,
                DayBucket = result.DayBucket,
                Slot = new NowSpinningSlotVm { Key = result.Slot.Key, Label = result.Slot.Label },
                Lanes = lanes,
            };
        }

        private static NowSpinningProgramLaneVm MapLane(NowSpinningProgramLaneDto lane)
        {
            return new NowSpinningProgramLaneVm
            {
                Mix = lane.Mix is not null ? MapMix(lane.Mix) : null,
                Schedule = lane.Schedule
                    .Select(s => new NowSpinningScheduleEntryVm
                    {
                        At = s.At,
                        Slot = new NowSpinningSlotVm { Key = s.Slot.Key, Label = s.Slot.Label },
                        DayBucket = s.DayBucket,
                        Mix = s.Mix is not null ? MapMix(s.Mix) : null,
                    })
                    .ToArray(),
                LeanIgnored = lane.LeanIgnored,
                SkipsIgnored = lane.SkipsIgnored,
                PoolFallback = lane.PoolFallback,
                NoMixAvailable = lane.NoMixAvailable,
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
