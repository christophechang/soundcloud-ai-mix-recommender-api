using System.Collections.Generic;
using Changsta.Ai.Core.BusinessProcesses.Radio;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Radio
{
    [TestFixture]
    public sealed class RadioStationDefinitionsTests
    {
        [TestCase("uk bass", "touchdown-fm")]
        [TestCase("breakbeat", "touchdown-fm")]
        [TestCase("ukg", "touchdown-fm")]
        [TestCase("hip-hop", "touchdown-fm")]
        [TestCase("hardcore", "touchdown-fm")]
        [TestCase("house", "deep-signal-fm")]
        [TestCase("deep-house", "deep-signal-fm")]
        [TestCase("electronica", "deep-signal-fm")]
        [TestCase("techno", "deep-signal-fm")]
        [TestCase("disco", "deep-signal-fm")]
        [TestCase("funk", "deep-signal-fm")]
        [TestCase("jungle", "jungle-pressure")]
        [TestCase("dnb", "jungle-pressure")]
        public void Genre_maps_to_correct_station(string genre, string expectedStationId)
        {
            bool found = RadioStationDefinitions.TryGetStationForGenre(genre, out string stationId);
            found.Should().BeTrue();
            stationId.Should().Be(expectedStationId);
        }

        [Test]
        public void Unknown_genre_returns_false()
        {
            bool found = RadioStationDefinitions.TryGetStationForGenre("ambient", out _);
            found.Should().BeFalse();
        }

        [Test]
        public void Genre_lookup_is_case_insensitive()
        {
            RadioStationDefinitions.TryGetStationForGenre("UK Bass", out string stationId).Should().BeTrue();
            stationId.Should().Be("touchdown-fm");
        }

        [Test]
        public void Touchdown_FM_is_default_station()
        {
            RadioStationDefinitions.DefaultStationId.Should().Be("touchdown-fm");
        }

        [Test]
        public void Exactly_three_stations_defined()
        {
            RadioStationDefinitions.Stations.Should().HaveCount(3);
        }

        [Test]
        public void Only_one_station_is_default()
        {
            int count = 0;
            foreach (var s in RadioStationDefinitions.Stations)
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
            foreach (var s in RadioStationDefinitions.Stations)
            {
                s.Frequency.Should().NotBeNullOrWhiteSpace(because: $"{s.Id} must have a frequency");
            }
        }

        [Test]
        public void Each_genre_belongs_to_exactly_one_station()
        {
            var all = new System.Collections.Generic.List<string>();
            foreach (var s in RadioStationDefinitions.Stations)
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
            int offset = RadioStationDefinitions.GetBpmOffset("jungle-pressure");
            offset.Should().BeGreaterThan(0, because: "DNB/Jungle BPM is much higher than the global slot targets");
        }

        [Test]
        public void Deep_signal_fm_bpm_offset_is_negative()
        {
            int offset = RadioStationDefinitions.GetBpmOffset("deep-signal-fm");
            offset.Should().BeLessThan(0, because: "House runs slower than the global slot targets");
        }

        [Test]
        public void Unknown_station_bpm_offset_returns_zero()
        {
            int offset = RadioStationDefinitions.GetBpmOffset("unknown-station");
            offset.Should().Be(0);
        }
    }
}
