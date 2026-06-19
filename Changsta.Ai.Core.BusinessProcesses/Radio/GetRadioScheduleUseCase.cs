using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Contracts.Radio;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    public sealed class GetRadioScheduleUseCase : IGetRadioScheduleUseCase
    {
        private const int CatalogMaxItems = 200;
        private const string ScheduleTimezoneId = "Europe/London";

        private static readonly IReadOnlyDictionary<string, string> GenreAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["deep-house"] = "house",
            };

        private readonly IMixCatalogueProvider _catalogueProvider;
        private readonly RadioScheduler _scheduler;
        private readonly TimeZoneInfo _scheduleTimezone;

        public GetRadioScheduleUseCase(
            IMixCatalogueProvider catalogueProvider,
            ILogger<GetRadioScheduleUseCase> logger)
        {
            _catalogueProvider = catalogueProvider ?? throw new ArgumentNullException(nameof(catalogueProvider));
            ArgumentNullException.ThrowIfNull(logger);
            _scheduler = new RadioScheduler();
            _scheduleTimezone = ResolveScheduleTimezone(ScheduleTimezoneId, logger);
        }

        public async Task<RadioScheduleResultDto> GetAsync(CancellationToken cancellationToken)
        {
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow.UtcDateTime, _scheduleTimezone);
            int currentHour = localNow.Hour;
            DateOnly scheduleDate = DateOnly.FromDateTime(localNow);

            IReadOnlyList<Mix> mixes = await _catalogueProvider
                .GetLatestAsync(CatalogMaxItems, cancellationToken)
                .ConfigureAwait(false);

            RadioSchedule schedule = _scheduler.Build(mixes, scheduleDate);

            IReadOnlyList<RadioScheduleViolation> violations = RadioScheduleValidator.Validate(schedule);

            var genreCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Mix mix in mixes)
            {
                string normalized = GenreAliases.TryGetValue(mix.Genre, out string? alias) ? alias : mix.Genre;
                genreCounts[normalized] = genreCounts.TryGetValue(normalized, out int existing) ? existing + 1 : 1;
            }

            var stations = new List<RadioStationScheduleDto>(RadioStationDefinitions.Stations.Count);

            foreach (RadioStation station in RadioStationDefinitions.Stations)
            {
                IReadOnlyList<RadioScheduledSlot> stationSlots = schedule.StationSlots[station.Id];

                RadioHourSlotDto currentSlot = MapSlot(stationSlots[currentHour], isCurrent: true);
                IReadOnlyList<RadioHourSlotDto> todaySlots = stationSlots
                    .Select(s => MapSlot(s, s.Hour == currentHour))
                    .ToArray();

                string[] sortedGenres = station.Genres
                    .Select(g => GenreAliases.TryGetValue(g, out string? alias) ? alias : g)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => genreCounts.TryGetValue(g, out int c) ? c : 0)
                    .ToArray();

                stations.Add(new RadioStationScheduleDto
                {
                    Id = station.Id,
                    Slug = station.Slug,
                    Strapline = station.Strapline,
                    Name = station.Name,
                    Frequency = station.Frequency,
                    Description = station.Description,
                    IsDefault = station.IsDefault,
                    Genres = sortedGenres,
                    CurrentSlot = currentSlot,
                    TodaySlots = todaySlots,
                });
            }

            return new RadioScheduleResultDto
            {
                GeneratedAtUtc = utcNow,
                ScheduleDate = scheduleDate.ToString("yyyy-MM-dd"),
                Timezone = ScheduleTimezoneId,
                CurrentHour = currentHour,
                DefaultStationId = RadioStationDefinitions.DefaultStationId,
                Stations = stations,
                ValidationWarnings = violations.Select(v => v.Description).ToArray(),
            };
        }

        // Resolved per instance (not in a static initialiser) so a host without tzdata
        // degrades to UTC with a logged warning rather than poisoning the type with a
        // TypeInitializationException for the rest of the process. See issue #48.
        internal static TimeZoneInfo ResolveScheduleTimezone(string timezoneId, ILogger logger)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                logger.LogWarning(
                    ex,
                    "Schedule timezone '{TimezoneId}' could not be resolved on this host; falling back to UTC. Radio slot hours may be offset.",
                    timezoneId);
                return TimeZoneInfo.Utc;
            }
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
