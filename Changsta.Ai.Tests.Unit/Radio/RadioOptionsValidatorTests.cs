using System;
using System.Collections.Generic;
using System.Linq;
using Changsta.Ai.Core.BusinessProcesses.Radio;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Radio
{
    [TestFixture]
    public sealed class RadioOptionsValidatorTests
    {
        [Test]
        public void The_shipped_configuration_is_valid()
        {
            // If this fails, the app would refuse to start — config/radio.json is the source.
            RadioOptionsValidator.Validate(RadioTestConfig.Options).Should().BeEmpty();
        }

        [Test]
        public void Rejects_an_empty_station_list()
        {
            var options = new RadioOptions { Stations = Array.Empty<RadioStationOptions>(), Slots = ValidSlots() };

            RadioOptionsValidator.Validate(options)
                .Should().ContainMatch("*at least one station*");
        }

        [Test]
        public void Rejects_a_station_with_no_genres()
        {
            RadioOptions options = ValidOptions();
            options.Stations = new[] { Station("140", genres: Array.Empty<string>()) };

            RadioOptionsValidator.Validate(options)
                .Should().ContainMatch("*has no genres*");
        }

        [Test]
        public void Rejects_a_non_canonical_genre()
        {
            RadioOptions options = ValidOptions();

            // "Drum & Bass" normalises to "dnb"; scheduling matches canonical values, so the
            // station would silently never schedule anything.
            options.Stations = new[] { Station("140", genres: new[] { "Drum & Bass" }) };

            RadioOptionsValidator.Validate(options)
                .Should().ContainMatch("*is not canonical*");
        }

        [Test]
        public void Rejects_an_unknown_energy_value()
        {
            RadioOptions options = ValidOptions();
            options.Slots = ValidSlots()
                .Select(s => s.Key == "Dead"
                    ? new RadioSlotOptions
                    {
                        Key = s.Key,
                        Label = s.Label,
                        BaseBpmTarget = s.BaseBpmTarget,
                        WarmthTarget = s.WarmthTarget,
                        EnergyValues = new[] { "supercharged" },
                    }
                    : s)
                .ToArray();

            RadioOptionsValidator.Validate(options)
                .Should().ContainMatch("*unknown energy value 'supercharged'*");
        }

        [Test]
        public void Rejects_a_missing_slot()
        {
            RadioOptions options = ValidOptions();
            options.Slots = ValidSlots().Where(s => s.Key != "Primetime").ToArray();

            RadioOptionsValidator.Validate(options)
                .Should().ContainMatch("*Primetime* is missing*");
        }

        [Test]
        public void Rejects_an_unknown_slot_key()
        {
            RadioOptions options = ValidOptions();
            options.Slots = ValidSlots()
                .Append(new RadioSlotOptions { Key = "Brunch", EnergyValues = new[] { "mid" } })
                .ToArray();

            RadioOptionsValidator.Validate(options)
                .Should().ContainMatch("*'Brunch' is not a known slot*");
        }

        [Test]
        public void Rejects_duplicate_station_ids()
        {
            RadioOptions options = ValidOptions();
            options.Stations = new[] { Station("140"), Station("140", isDefault: false) };

            RadioOptionsValidator.Validate(options)
                .Should().ContainMatch("*declared more than once*");
        }

        [TestCase(0)]
        [TestCase(2)]
        public void Requires_exactly_one_default_station(int defaults)
        {
            RadioOptions options = ValidOptions();
            options.Stations = new[]
            {
                Station("140", isDefault: defaults >= 1),
                Station("170", isDefault: defaults >= 2),
            };

            RadioOptionsValidator.Validate(options)
                .Should().ContainMatch("Exactly one radio station must be marked IsDefault*");
        }

        private static RadioOptions ValidOptions() => new RadioOptions
        {
            Stations = new[] { Station("140") },
            Slots = ValidSlots(),
        };

        private static RadioStationOptions Station(
            string id,
            bool isDefault = true,
            IReadOnlyList<string>? genres = null) => new RadioStationOptions
            {
                Id = id,
                Slug = "slug-" + id,
                Strapline = "strapline",
                Name = "Station " + id,
                Frequency = "100.0 FM",
                Description = "description",
                IsDefault = isDefault,
                Genres = genres ?? new[] { "dnb" },
            };

        private static RadioSlotOptions[] ValidSlots() => new[]
        {
            Slot("Dead", "peak"),
            Slot("Comedown", "chilled"),
            Slot("Morning", "mid"),
            Slot("Afternoon", "mid"),
            Slot("EarlyEve", "mid"),
            Slot("Primetime", "peak"),
        };

        private static RadioSlotOptions Slot(string key, string energy) => new RadioSlotOptions
        {
            Key = key,
            Label = key.ToLowerInvariant(),
            BaseBpmTarget = 120,
            WarmthTarget = 0.0,
            EnergyValues = new[] { energy },
        };
    }
}
