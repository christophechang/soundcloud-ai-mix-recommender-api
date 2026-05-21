using System.Collections.Generic;
using Changsta.Ai.Core.BusinessProcesses.Radio;
using Changsta.Ai.Core.Domain;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Radio
{
    [TestFixture]
    public sealed class RadioScheduleValidatorTests
    {
        private static readonly System.DateOnly Today = new System.DateOnly(2026, 5, 21);

        [Test]
        public void Valid_schedule_produces_no_violations()
        {
            RadioScheduleValidator.Validate(ValidSchedule()).Should().BeEmpty();
        }

        [Test]
        public void Missing_slots_produces_SlotCountMismatch()
        {
            var violations = RadioScheduleValidator.Validate(ScheduleWithMissingSlot("touchdown-fm"));
            violations.Should().Contain(v =>
                v.StationId == "touchdown-fm" &&
                v.Rule == RadioScheduleRule.SlotCountMismatch);
        }

        [Test]
        public void Wrong_genre_on_station_produces_GenreMismatch()
        {
            var violations = RadioScheduleValidator.Validate(ScheduleWithWrongGenre("touchdown-fm", "house"));
            violations.Should().Contain(v =>
                v.StationId == "touchdown-fm" &&
                v.Rule == RadioScheduleRule.GenreMismatch);
        }

        [Test]
        public void Repeated_mix_on_same_station_produces_SameStationSameDayRepeat()
        {
            var violations = RadioScheduleValidator.Validate(ScheduleWithRepeat("touchdown-fm"));
            violations.Should().Contain(v =>
                v.StationId == "touchdown-fm" &&
                v.Rule == RadioScheduleRule.SameStationSameDayRepeat);
        }

        [Test]
        public void Same_mix_in_same_hour_across_stations_produces_SameHourCrossStation()
        {
            var violations = RadioScheduleValidator.Validate(ScheduleWithCrossStationConflict());
            violations.Should().Contain(v =>
                v.Rule == RadioScheduleRule.SameHourCrossStationDuplicate);
        }

        private static RadioSchedule ValidSchedule()
        {
            int counter = 0;
            var slots = new Dictionary<string, IReadOnlyList<RadioScheduledSlot>>();
            foreach (RadioStation station in RadioStationDefinitions.Stations)
            {
                string genre = station.Genres[0];
                var list = new List<RadioScheduledSlot>();
                for (int h = 0; h < 24; h++)
                {
                    list.Add(Slot(h, Mix($"m{counter++}", genre)));
                }

                slots[station.Id] = list;
            }

            return new RadioSchedule { ScheduleDate = Today, StationSlots = slots };
        }

        private static RadioSchedule ScheduleWithMissingSlot(string target)
        {
            int counter = 0;
            var slots = new Dictionary<string, IReadOnlyList<RadioScheduledSlot>>();
            foreach (RadioStation station in RadioStationDefinitions.Stations)
            {
                string genre = station.Genres[0];
                var list = new List<RadioScheduledSlot>();
                int count = station.Id == target ? 23 : 24;
                for (int h = 0; h < count; h++)
                {
                    list.Add(Slot(h, Mix($"m{counter++}", genre)));
                }

                slots[station.Id] = list;
            }

            return new RadioSchedule { ScheduleDate = Today, StationSlots = slots };
        }

        private static RadioSchedule ScheduleWithWrongGenre(string target, string wrongGenre)
        {
            int counter = 0;
            var slots = new Dictionary<string, IReadOnlyList<RadioScheduledSlot>>();
            foreach (RadioStation station in RadioStationDefinitions.Stations)
            {
                string genre = station.Genres[0];
                var list = new List<RadioScheduledSlot>();
                for (int h = 0; h < 24; h++)
                {
                    string g = station.Id == target && h == 0 ? wrongGenre : genre;
                    list.Add(Slot(h, Mix($"m{counter++}", g)));
                }

                slots[station.Id] = list;
            }

            return new RadioSchedule { ScheduleDate = Today, StationSlots = slots };
        }

        private static RadioSchedule ScheduleWithRepeat(string target)
        {
            int counter = 0;
            Mix repeated = Mix("repeated", "uk bass");
            var slots = new Dictionary<string, IReadOnlyList<RadioScheduledSlot>>();
            foreach (RadioStation station in RadioStationDefinitions.Stations)
            {
                string genre = station.Genres[0];
                var list = new List<RadioScheduledSlot>();
                for (int h = 0; h < 24; h++)
                {
                    Mix m = station.Id == target && h < 2 ? repeated : Mix($"m{counter++}", genre);
                    list.Add(Slot(h, m));
                }

                slots[station.Id] = list;
            }

            return new RadioSchedule { ScheduleDate = Today, StationSlots = slots };
        }

        private static RadioSchedule ScheduleWithCrossStationConflict()
        {
            Mix shared = Mix("shared", "uk bass");
            int counter = 0;
            var slots = new Dictionary<string, IReadOnlyList<RadioScheduledSlot>>();
            foreach (RadioStation station in RadioStationDefinitions.Stations)
            {
                string genre = station.Genres[0];
                var list = new List<RadioScheduledSlot>();
                for (int h = 0; h < 24; h++)
                {
                    list.Add(Slot(h, h == 5 ? shared : Mix($"m{counter++}", genre)));
                }

                slots[station.Id] = list;
            }

            return new RadioSchedule { ScheduleDate = Today, StationSlots = slots };
        }

        private static RadioScheduledSlot Slot(int hour, Mix mix) =>
            new RadioScheduledSlot { Hour = hour, Mix = mix, Score = new RadioSlotScore() };

        private static Mix Mix(string id, string genre) => new Mix
        {
            Id = id,
            Title = $"A - {id}",
            Url = $"https://sc.test/{id}",
            Genre = genre,
            Energy = "mid",
            BpmMin = 125,
        };
    }
}
