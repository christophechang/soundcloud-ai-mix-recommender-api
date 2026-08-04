using System;
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
        public void Bpm_target_always_lands_inside_the_station_pool()
        {
            // A shared absolute curve used to ask a 10-BPM-wide catalogue for a 62-BPM swing, so
            // most slots targeted a tempo the station had no records at. Deriving from the pool
            // and clamping to it makes every target reachable by construction.
            int[] narrow = { 164, 168, 170, 172, 174 };

            foreach (SlotKey slot in Enum.GetValues<SlotKey>())
            {
                foreach (DayBucket day in Enum.GetValues<DayBucket>())
                {
                    int target = RadioTestConfig.Definitions.GetBpmTarget(slot, day, narrow);
                    target.Should().BeInRange(narrow[0], narrow[^1]);
                }
            }
        }

        [Test]
        public void Bpm_target_rises_across_the_day_from_comedown_to_dead()
        {
            int[] pool = { 100, 110, 120, 130, 140, 150, 160 };

            int comedown = RadioTestConfig.Definitions.GetBpmTarget(SlotKey.Comedown, DayBucket.Weeknight, pool);
            int morning = RadioTestConfig.Definitions.GetBpmTarget(SlotKey.Morning, DayBucket.Weeknight, pool);
            int primetime = RadioTestConfig.Definitions.GetBpmTarget(SlotKey.Primetime, DayBucket.Weeknight, pool);
            int dead = RadioTestConfig.Definitions.GetBpmTarget(SlotKey.Dead, DayBucket.Weeknight, pool);

            comedown.Should().BeLessThan(morning);
            morning.Should().BeLessThan(primetime);
            primetime.Should().BeLessThanOrEqualTo(dead);
        }

        [Test]
        public void Bpm_target_is_zero_when_the_station_has_no_usable_bpms()
        {
            int target = RadioTestConfig.Definitions.GetBpmTarget(
                SlotKey.Morning, DayBucket.Weeknight, Array.Empty<int>());

            target.Should().Be(0);
        }

        [TestCase(0, 100)]
        [TestCase(50, 130)]
        [TestCase(100, 160)]
        public void Percentile_interpolates_over_the_ascending_pool(int percentile, int expected)
        {
            int[] pool = { 100, 110, 120, 130, 140, 150, 160 };
            RadioDefinitions.Percentile(pool, percentile).Should().Be(expected);
        }
    }
}
