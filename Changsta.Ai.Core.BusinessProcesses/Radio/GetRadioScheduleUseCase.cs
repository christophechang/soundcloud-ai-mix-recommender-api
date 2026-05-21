using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Contracts.Radio;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    public sealed class GetRadioScheduleUseCase : IGetRadioScheduleUseCase
    {
        private const int CatalogMaxItems = 200;

        private readonly IMixCatalogueProvider _catalogueProvider;
        private readonly RadioScheduler _scheduler;

        public GetRadioScheduleUseCase(IMixCatalogueProvider catalogueProvider)
        {
            _catalogueProvider = catalogueProvider ?? throw new ArgumentNullException(nameof(catalogueProvider));
            _scheduler = new RadioScheduler();
        }

        public async Task<RadioScheduleResultDto> GetAsync(CancellationToken cancellationToken)
        {
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            int currentHour = utcNow.Hour;
            DateOnly scheduleDate = DateOnly.FromDateTime(utcNow.UtcDateTime);

            IReadOnlyList<Mix> mixes = await _catalogueProvider
                .GetLatestAsync(CatalogMaxItems, cancellationToken)
                .ConfigureAwait(false);

            RadioSchedule schedule = _scheduler.Build(mixes, scheduleDate);

            IReadOnlyList<RadioScheduleViolation> violations = RadioScheduleValidator.Validate(schedule);

            var stations = new List<RadioStationScheduleDto>(RadioStationDefinitions.Stations.Count);

            foreach (RadioStation station in RadioStationDefinitions.Stations)
            {
                IReadOnlyList<RadioScheduledSlot> stationSlots = schedule.StationSlots[station.Id];

                RadioHourSlotDto currentSlot = MapSlot(stationSlots[currentHour], isCurrent: true);
                IReadOnlyList<RadioHourSlotDto> todaySlots = stationSlots
                    .Select(s => MapSlot(s, s.Hour == currentHour))
                    .ToArray();

                stations.Add(new RadioStationScheduleDto
                {
                    Id = station.Id,
                    Name = station.Name,
                    Frequency = station.Frequency,
                    Description = station.Description,
                    IsDefault = station.IsDefault,
                    CurrentSlot = currentSlot,
                    TodaySlots = todaySlots,
                });
            }

            return new RadioScheduleResultDto
            {
                GeneratedAtUtc = utcNow,
                ScheduleDate = scheduleDate.ToString("yyyy-MM-dd"),
                Timezone = "UTC",
                CurrentHour = currentHour,
                DefaultStationId = RadioStationDefinitions.DefaultStationId,
                Stations = stations,
                ValidationWarnings = violations.Select(v => v.Description).ToArray(),
            };
        }

        private static RadioHourSlotDto MapSlot(RadioScheduledSlot slot, bool isCurrent) =>
            new RadioHourSlotDto
            {
                Hour = slot.Hour,
                Mix = slot.Mix,
                IsCurrent = isCurrent,
                AuditWarnings = slot.AuditWarnings,
                RelaxedRules = slot.RelaxedRules,
            };
    }
}
