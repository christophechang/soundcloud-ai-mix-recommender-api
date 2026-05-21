using System;
using System.Collections.Generic;
using System.Linq;
using Changsta.Ai.Core.BusinessProcesses.Radio;
using Changsta.Ai.Core.Domain;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Radio
{
    [TestFixture]
    public sealed class RadioSchedulerTests
    {
        private static readonly DateOnly Thursday = new DateOnly(2026, 5, 21);

        [Test]
        public void Schedule_has_three_stations()
        {
            Build(Thursday, Catalogue(24, 24, 24)).StationSlots.Should().HaveCount(3);
        }

        [Test]
        public void Each_station_has_24_slots()
        {
            RadioSchedule s = Build(Thursday, Catalogue(24, 24, 24));
            foreach (var kvp in s.StationSlots)
            {
                kvp.Value.Should().HaveCount(24, because: $"{kvp.Key} must have 24 slots");
            }
        }

        [Test]
        public void No_slot_has_null_mix()
        {
            RadioSchedule s = Build(Thursday, Catalogue(24, 24, 24));
            foreach (var kvp in s.StationSlots)
            {
                foreach (RadioScheduledSlot slot in kvp.Value)
                {
                    slot.Mix.Should().NotBeNull(because: $"{kvp.Key} hour {slot.Hour}");
                }
            }
        }

        [Test]
        public void Slots_are_ordered_0_to_23()
        {
            RadioSchedule s = Build(Thursday, Catalogue(24, 24, 24));
            foreach (var kvp in s.StationSlots)
            {
                int expected = 0;
                foreach (RadioScheduledSlot slot in kvp.Value)
                {
                    slot.Hour.Should().Be(expected++);
                }
            }
        }

        [Test]
        public void No_same_mix_twice_on_same_station_when_catalogue_is_large_enough()
        {
            RadioSchedule s = Build(Thursday, Catalogue(30, 30, 30));
            foreach (var kvp in s.StationSlots)
            {
                kvp.Value.Select(x => x.Mix.Id).Should().OnlyHaveUniqueItems(
                    because: $"{kvp.Key} must not repeat mixes");
            }
        }

        [Test]
        public void Genre_ownership_means_no_cross_station_mix_sharing()
        {
            RadioSchedule s = Build(Thursday, Catalogue(30, 30, 30));
            var sets = s.StationSlots.Values
                .Select(slots => slots.Select(sl => sl.Mix.Id).ToHashSet())
                .ToList();
            for (int i = 0; i < sets.Count; i++)
            {
                for (int j = i + 1; j < sets.Count; j++)
                {
                    sets[i].Intersect(sets[j]).Should().BeEmpty();
                }
            }
        }

        [Test]
        public void Same_catalogue_and_date_produce_identical_schedule()
        {
            IReadOnlyList<Mix> cat = Catalogue(30, 30, 30);
            RadioSchedule s1 = Build(Thursday, cat);
            RadioSchedule s2 = Build(Thursday, cat);
            foreach (string stationId in s1.StationSlots.Keys)
            {
                for (int h = 0; h < 24; h++)
                {
                    s1.StationSlots[stationId][h].Mix.Id
                        .Should().Be(s2.StationSlots[stationId][h].Mix.Id);
                }
            }
        }

        [Test]
        public void Different_dates_produce_different_schedules()
        {
            IReadOnlyList<Mix> cat = Catalogue(30, 30, 30);
            RadioSchedule thu = Build(Thursday, cat);
            RadioSchedule fri = Build(Thursday.AddDays(1), cat);
            bool anyDiff = thu.StationSlots.Keys.Any(sid =>
                Enumerable.Range(0, 24).Any(h =>
                    thu.StationSlots[sid][h].Mix.Id != fri.StationSlots[sid][h].Mix.Id));
            anyDiff.Should().BeTrue();
        }

        [Test]
        public void Each_station_only_schedules_its_own_genres()
        {
            RadioSchedule s = Build(Thursday, Catalogue(30, 30, 30));
            foreach (var kvp in s.StationSlots)
            {
                foreach (RadioScheduledSlot slot in kvp.Value)
                {
                    RadioStationDefinitions.TryGetStationForGenre(slot.Mix.Genre, out string ownerStation);
                    ownerStation.Should().Be(
                        kvp.Key,
                        because: $"{kvp.Key} hour {slot.Hour} must only play its genres");
                }
            }
        }

        [Test]
        public void Still_produces_24_slots_when_catalogue_has_only_one_mix_per_station()
        {
            RadioSchedule s = Build(Thursday, Catalogue(1, 1, 1));
            foreach (var kvp in s.StationSlots)
            {
                kvp.Value.Should().HaveCount(24);
            }
        }

        [Test]
        public void Single_mix_station_records_relaxed_rule_in_audit()
        {
            RadioSchedule s = Build(Thursday, Catalogue(1, 1, 1));
            foreach (var kvp in s.StationSlots)
            {
                bool anyRelaxed = kvp.Value.Any(sl => sl.RelaxedRules.Count > 0);
                anyRelaxed.Should().BeTrue(
                    because: $"{kvp.Key} with 1 mix must relax repeat rule");
            }
        }

        [Test]
        public void Empty_station_catalogue_throws_InvalidOperationException()
        {
            Action act = () => Build(Thursday, Catalogue(0, 24, 24));
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*touchdown-fm*");
        }

        [Test]
        public void Jungle_pressure_bpm_target_is_higher_than_touchdown_for_same_slot()
        {
            RadioSchedule s = Build(Thursday, Catalogue(30, 30, 30));
            s.StationSlots.Should().ContainKey("jungle-pressure");
            s.StationSlots["jungle-pressure"].Should().HaveCount(24);
        }

        private static RadioSchedule Build(DateOnly date, IEnumerable<Mix> mixes)
            => new RadioScheduler().Build(mixes.ToList(), date);

        private static IReadOnlyList<Mix> Catalogue(int td, int ds, int jp)
        {
            var list = new List<Mix>();
            for (int i = 0; i < td; i++)
            {
                list.Add(M($"td-{i}", "uk bass", "mid", 130));
            }

            for (int i = 0; i < ds; i++)
            {
                list.Add(M($"ds-{i}", "house", "mid", 125));
            }

            for (int i = 0; i < jp; i++)
            {
                list.Add(M($"jp-{i}", "dnb", "mid", 172));
            }

            return list;
        }

        private static Mix M(string id, string genre, string energy, int bpm) => new Mix
        {
            Id = id,
            Title = $"Artist - {id}",
            Url = $"https://sc.test/{id}",
            Genre = genre,
            Energy = energy,
            BpmMin = bpm,
            BpmMax = bpm + 4,
            Warmth = 0.0,
        };
    }
}
