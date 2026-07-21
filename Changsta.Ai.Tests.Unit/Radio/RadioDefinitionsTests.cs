using System.Collections.Generic;
using Changsta.Ai.Core.BusinessProcesses.Radio;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Radio
{
    [TestFixture]
    public sealed class RadioDefinitionsTests
    {
        [TestCase("uk bass", "140")]
        [TestCase("breakbeat", "140")]
        [TestCase("ukg", "140")]
        [TestCase("hip-hop", "140")]
        [TestCase("hardcore", "140")]
        [TestCase("house", "4x4")]
        [TestCase("deep-house", "4x4")]
        [TestCase("electronica", "4x4")]
        [TestCase("techno", "4x4")]
        [TestCase("disco", "4x4")]
        [TestCase("funk", "4x4")]
        [TestCase("jungle", "170")]
        [TestCase("dnb", "170")]
        public void Genre_maps_to_correct_station(string genre, string expectedStationId)
        {
            bool found = RadioTestConfig.Definitions.TryGetStationForGenre(genre, out string stationId);
            found.Should().BeTrue();
            stationId.Should().Be(expectedStationId);
        }

        [Test]
        public void Unknown_genre_returns_false()
        {
            bool found = RadioTestConfig.Definitions.TryGetStationForGenre("ambient", out _);
            found.Should().BeFalse();
        }

        [Test]
        public void Genre_lookup_is_case_insensitive()
        {
            RadioTestConfig.Definitions.TryGetStationForGenre("UK Bass", out string stationId).Should().BeTrue();
            stationId.Should().Be("140");
        }

        [Test]
        public void Touchdown_FM_is_default_station()
        {
            RadioTestConfig.Definitions.DefaultStationId.Should().Be("140");
        }

        [Test]
        public void Exactly_three_stations_defined()
        {
            RadioTestConfig.Definitions.Stations.Should().HaveCount(3);
        }

        [Test]
        public void Only_one_station_is_default()
        {
            int count = 0;
            foreach (var s in RadioTestConfig.Definitions.Stations)
            {
                if (s.IsDefault)
                {
                    count++;
                }
            }

            count.Should().Be(1);
        }

        [Test]
        public void Each_station_has_non_empty_frequency()
        {
            foreach (var s in RadioTestConfig.Definitions.Stations)
            {
                s.Frequency.Should().NotBeNullOrWhiteSpace(because: $"{s.Id} must have a frequency");
            }
        }

        [Test]
        public void Each_genre_belongs_to_exactly_one_station()
        {
            var all = new System.Collections.Generic.List<string>();
            foreach (var s in RadioTestConfig.Definitions.Stations)
            {
                foreach (string g in s.Genres)
                {
                    all.Add(g);
                }
            }

            all.Should().OnlyHaveUniqueItems();
        }

        [Test]
        public void Jungle_pressure_bpm_offset_is_positive()
        {
            int offset = RadioTestConfig.Definitions.GetBpmOffset("170");
            offset.Should().BeGreaterThan(0, because: "DNB/Jungle BPM is much higher than the global slot targets");
        }

        [Test]
        public void Deep_signal_fm_bpm_offset_is_negative()
        {
            int offset = RadioTestConfig.Definitions.GetBpmOffset("4x4");
            offset.Should().BeLessThan(0, because: "House runs slower than the global slot targets");
        }

        [Test]
        public void Unknown_station_bpm_offset_returns_zero()
        {
            int offset = RadioTestConfig.Definitions.GetBpmOffset("unknown-station");
            offset.Should().Be(0);
        }
    }
}
