using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.BusinessProcesses.Radio;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Radio
{
    [TestFixture]
    public sealed class GetRadioScheduleUseCaseTests
    {
        [Test]
        public async Task GetAsync_returns_three_stations()
        {
            RadioScheduleResultDto r = await Run(Catalogue());
            r.Stations.Should().HaveCount(3);
        }

        [Test]
        public async Task GetAsync_default_station_id_is_touchdown_fm()
        {
            RadioScheduleResultDto r = await Run(Catalogue());
            r.DefaultStationId.Should().Be("140");
        }

        [Test]
        public async Task GetAsync_exactly_one_station_has_IsDefault_true()
        {
            RadioScheduleResultDto r = await Run(Catalogue());
            r.Stations.Where(s => s.IsDefault).Should().HaveCount(1);
            r.Stations.Single(s => s.IsDefault).Id.Should().Be("140");
        }

        [Test]
        public async Task GetAsync_each_station_has_current_slot_marked()
        {
            RadioScheduleResultDto r = await Run(Catalogue());
            foreach (RadioStationScheduleDto station in r.Stations)
            {
                station.CurrentSlot.IsCurrent.Should().BeTrue();
            }
        }

        [Test]
        public async Task GetAsync_current_slot_hour_matches_CurrentHour()
        {
            RadioScheduleResultDto r = await Run(Catalogue());
            foreach (RadioStationScheduleDto station in r.Stations)
            {
                station.CurrentSlot.Hour.Should().Be(r.CurrentHour);
            }
        }

        [Test]
        public async Task GetAsync_each_station_has_metadata()
        {
            RadioScheduleResultDto r = await Run(Catalogue());
            foreach (RadioStationScheduleDto station in r.Stations)
            {
                station.Id.Should().NotBeNullOrEmpty();
                station.Name.Should().NotBeNullOrEmpty();
                station.Frequency.Should().NotBeNullOrEmpty();
                station.Description.Should().NotBeNullOrEmpty();
            }
        }

        [Test]
        public async Task GetAsync_schedule_date_and_timezone_present()
        {
            RadioScheduleResultDto r = await Run(Catalogue());
            r.ScheduleDate.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$");
            r.Timezone.Should().Be("Europe/London");
        }

        [Test]
        public async Task GetAsync_today_slots_has_24_entries()
        {
            RadioScheduleResultDto r = await Run(Catalogue());
            foreach (RadioStationScheduleDto station in r.Stations)
            {
                station.TodaySlots.Should().HaveCount(24);
            }
        }

        [Test]
        public void ResolveScheduleTimezone_falls_back_to_utc_and_warns_when_id_is_unknown()
        {
            var logger = new ListLogger();

            TimeZoneInfo resolved = GetRadioScheduleUseCase.ResolveScheduleTimezone("Not/AZone", logger);

            resolved.Should().Be(TimeZoneInfo.Utc);
            logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning)
                .Which.Message.Should().Contain("Not/AZone");
        }

        [Test]
        public void ResolveScheduleTimezone_resolves_known_id_without_warning()
        {
            var logger = new ListLogger();

            TimeZoneInfo resolved = GetRadioScheduleUseCase.ResolveScheduleTimezone("Europe/London", logger);

            resolved.Should().NotBeNull();
            logger.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
        }

        private static Task<RadioScheduleResultDto> Run(IReadOnlyList<Mix> mixes)
            => new GetRadioScheduleUseCase(new StubCatalogue(mixes), NullLogger<GetRadioScheduleUseCase>.Instance)
                .GetAsync(CancellationToken.None);

        private static IReadOnlyList<Mix> Catalogue()
        {
            var list = new List<Mix>();
            for (int i = 0; i < 24; i++)
            {
                list.Add(M($"td{i}", "uk bass", 130));
            }

            for (int i = 0; i < 24; i++)
            {
                list.Add(M($"ds{i}", "house", 125));
            }

            for (int i = 0; i < 24; i++)
            {
                list.Add(M($"jp{i}", "dnb", 172));
            }

            return list;
        }

        private static Mix M(string id, string genre, int bpm) => new Mix
        {
            Id = id,
            Title = $"Artist - {id}",
            Url = $"https://sc.test/{id}",
            Genre = genre,
            Energy = "mid",
            BpmMin = bpm,
            BpmMax = bpm + 4,
            Warmth = 0.0,
        };

        private sealed class StubCatalogue : IMixCatalogueProvider
        {
            private readonly IReadOnlyList<Mix> _mixes;

            internal StubCatalogue(IReadOnlyList<Mix> mixes) => _mixes = mixes;

            public Task<IReadOnlyList<Mix>> GetLatestAsync(int maxItems, CancellationToken ct)
                => Task.FromResult(_mixes);
        }

        private sealed class ListLogger : ILogger
        {
            public List<LogEntry> Entries { get; } = new();

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
                => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => Entries.Add(new LogEntry(logLevel, formatter(state, exception)));

            public sealed class LogEntry
            {
                public LogEntry(LogLevel level, string message)
                {
                    Level = level;
                    Message = message;
                }

                public LogLevel Level { get; }

                public string Message { get; }
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();

                public void Dispose()
                {
                }
            }
        }
    }
}
