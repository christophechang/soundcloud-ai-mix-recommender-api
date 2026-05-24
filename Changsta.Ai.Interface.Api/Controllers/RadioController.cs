using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Radio;
using Changsta.Ai.Core.Dtos;
using Changsta.Ai.Core.Exceptions;
using Changsta.Ai.Interface.Api.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Changsta.Ai.Interface.Api.Controllers
{
    [ApiController]
    [Route("api/radio")]
    [Produces("application/json")]
    public sealed class RadioController : ControllerBase
    {
        private readonly IGetRadioScheduleUseCase _useCase;

        public RadioController(IGetRadioScheduleUseCase useCase)
        {
            _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        }

        [HttpGet("stations")]
        public async Task<IActionResult> GetStationsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                RadioScheduleResultDto result = await _useCase
                    .GetAsync(cancellationToken)
                    .ConfigureAwait(false);

                return Ok(MapToResponse(result));
            }
            catch (RadioStationUnavailableException ex)
            {
                return StatusCode(503, new { error = ex.Message, stationId = ex.StationId });
            }
        }

        private static RadioResponse MapToResponse(RadioScheduleResultDto r) =>
            new RadioResponse
            {
                GeneratedAtUtc = r.GeneratedAtUtc,
                ScheduleDate = r.ScheduleDate,
                Timezone = r.Timezone,
                CurrentHour = r.CurrentHour,
                DefaultStationId = r.DefaultStationId,
                Stations = r.Stations.Select(MapStation).ToArray(),
            };

        private static RadioStationVm MapStation(RadioStationScheduleDto s) =>
            new RadioStationVm
            {
                Id = s.Id,
                Slug = s.Slug,
                Strapline = s.Strapline,
                Name = s.Name,
                Frequency = s.Frequency,
                Description = s.Description,
                IsDefault = s.IsDefault,
                Genres = s.Genres,
                CurrentSlot = MapSlot(s.CurrentSlot),
                TodaySlots = s.TodaySlots.Select(MapSlot).ToArray(),
            };

        private static RadioSlotVm MapSlot(RadioHourSlotDto slot) =>
            new RadioSlotVm
            {
                Hour = slot.Hour,
                Mix = RadioMixMapper.MapMix(slot.Mix),
                IsCurrent = slot.IsCurrent,
                Warnings = slot.AuditWarnings,
            };
    }
}
