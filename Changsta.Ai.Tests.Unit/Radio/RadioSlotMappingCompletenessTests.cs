using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Radio;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;
using Changsta.Ai.Interface.Api.Controllers;
using Changsta.Ai.Interface.Api.ViewModels;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Radio
{
    /// <summary>
    /// Guards the mapping layer rather than the serialiser.
    /// <para>
    /// <c>RadioSlotVm.RelaxedRules</c> shipped unmapped: the scheduler populated it, the use case
    /// carried it onto <see cref="RadioHourSlotDto"/>, and <c>RadioController.MapSlot</c> simply
    /// did not copy it. The property still existed and still serialised — as an empty array — so
    /// no serialisation or schema test could see the omission. Only feeding a fully-populated DTO
    /// through the controller and asserting nothing arrives at its default catches this class of
    /// bug, and it catches it for fields that do not exist yet.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class RadioSlotMappingCompletenessTests
    {
        [Test]
        public async Task Every_slot_field_survives_the_mapping_layer()
        {
            var ok = (OkObjectResult)await MakeController().GetStationsAsync(CancellationToken.None);
            var response = (RadioResponse)ok.Value!;
            RadioSlotVm slot = response.Stations[0].CurrentSlot;

            foreach (PropertyInfo property in typeof(RadioSlotVm)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                object? value = property.GetValue(slot);

                value.Should().NotBeNull(
                    because: $"RadioSlotVm.{property.Name} was populated on the DTO, so the mapping must carry it");

                if (value is string text)
                {
                    text.Should().NotBeEmpty(
                        because: $"RadioSlotVm.{property.Name} was populated on the DTO");
                }
                else if (value is IEnumerable sequence and not string)
                {
                    sequence.Cast<object>().Should().NotBeEmpty(
                        because: $"RadioSlotVm.{property.Name} was populated on the DTO, so an empty "
                               + "collection here means the mapping dropped it");
                }
            }
        }

        private static RadioController MakeController() =>
            new RadioController(new StubUseCase(MakeFullyPopulatedResult()));

        /// <summary>
        /// Every field on the slot DTO carries a non-default value, so anything arriving at its
        /// default on the far side was lost in mapping rather than never set.
        /// </summary>
        private static RadioScheduleResultDto MakeFullyPopulatedResult()
        {
            var now = new DateTimeOffset(2026, 8, 4, 9, 0, 0, TimeSpan.Zero);

            var slot = new RadioHourSlotDto
            {
                Hour = 9,
                IsCurrent = true,
                AuditWarnings = new[] { "Unknown energy value 'intense' treated as neutral." },
                RelaxedRules = new[] { "Score threshold relaxed — picking from any unused mix." },
                Mix = new Mix
                {
                    Id = "mix-1",
                    Title = "A - mix-1",
                    Url = "https://sc.test/mix-1",
                    Intro = "Intro copy.",
                    Genre = "breakbeat",
                    Energy = "mid",
                    BpmMin = 130,
                    BpmMax = 136,
                    Tracklist = new[]
                    {
                        new Track { Artist = "Artist 1", Title = "Track 1", CuePointSeconds = 0 },
                    },
                },
            };

            return new RadioScheduleResultDto
            {
                GeneratedAtUtc = now,
                ScheduleDate = "2026-08-04",
                Timezone = "Europe/London",
                CurrentHour = 9,
                DefaultStationId = "140",
                Stations = new[]
                {
                    new RadioStationScheduleDto
                    {
                        Id = "140",
                        Slug = "tooz-fm",
                        Strapline = "For the Breaks Headz",
                        Name = "Tooz FM",
                        Frequency = "103.5 FM",
                        Description = "UK Bass, Garage, Breaks, Hip-Hop and Hardcore",
                        IsDefault = true,
                        CurrentSlot = slot,
                    },
                },
            };
        }

        private sealed class StubUseCase : IGetRadioScheduleUseCase
        {
            private readonly RadioScheduleResultDto _result;

            internal StubUseCase(RadioScheduleResultDto result) => _result = result;

            public Task<RadioScheduleResultDto> GetAsync(CancellationToken ct) => Task.FromResult(_result);
        }
    }
}
