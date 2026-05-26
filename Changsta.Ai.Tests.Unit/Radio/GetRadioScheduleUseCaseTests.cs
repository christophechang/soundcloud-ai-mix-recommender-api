using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.BusinessProcesses.Radio;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;
using FluentAssertions;
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

        private static Task<RadioScheduleResultDto> Run(IReadOnlyList<Mix> mixes)
            => new GetRadioScheduleUseCase(new StubCatalogue(mixes))
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
    }
}
