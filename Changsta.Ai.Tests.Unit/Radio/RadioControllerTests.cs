using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.BusinessProcesses.Radio;
using Changsta.Ai.Core.Contracts.Radio;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;
using Changsta.Ai.Core.Exceptions;
using Changsta.Ai.Interface.Api.Controllers;
using Changsta.Ai.Interface.Api.ViewModels;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Radio
{
    [TestFixture]
    public sealed class RadioControllerTests
    {
        [Test]
        public async Task GetStationsAsync_returns_200()
        {
            IActionResult result = await MakeController().GetStationsAsync(CancellationToken.None);
            result.Should().BeOfType<OkObjectResult>();
        }

        [Test]
        public async Task GetStationsAsync_response_has_three_stations()
        {
            var ok = (OkObjectResult)await MakeController().GetStationsAsync(CancellationToken.None);
            var response = (RadioResponse)ok.Value!;
            response.Stations.Should().HaveCount(3);
        }

        [Test]
        public async Task GetStationsAsync_default_station_id_is_touchdown_fm()
        {
            var ok = (OkObjectResult)await MakeController().GetStationsAsync(CancellationToken.None);
            var response = (RadioResponse)ok.Value!;
            response.DefaultStationId.Should().Be("140");
        }

        [Test]
        public async Task GetStationsAsync_each_station_has_current_slot()
        {
            var ok = (OkObjectResult)await MakeController().GetStationsAsync(CancellationToken.None);
            var response = (RadioResponse)ok.Value!;
            foreach (RadioStationVm station in response.Stations)
            {
                station.CurrentSlot.Should().NotBeNull();
            }
        }

        [Test]
        public async Task GetStationsAsync_current_slot_mix_includes_intro()
        {
            var ok = (OkObjectResult)await MakeController().GetStationsAsync(CancellationToken.None);
            var response = (RadioResponse)ok.Value!;
            foreach (RadioStationVm station in response.Stations)
            {
                station.CurrentSlot.Mix.Intro.Should().Be($"Intro copy for mix-{station.Id}.");
            }
        }

        [Test]
        public async Task GetStationsAsync_each_station_has_frequency()
        {
            var ok = (OkObjectResult)await MakeController().GetStationsAsync(CancellationToken.None);
            var response = (RadioResponse)ok.Value!;
            foreach (RadioStationVm station in response.Stations)
            {
                station.Frequency.Should().NotBeNullOrEmpty();
            }
        }

        [Test]
        public async Task GetStationsAsync_returns_503_when_station_unavailable()
        {
            var controller = new RadioController(new ThrowingUseCase());
            IActionResult result = await controller.GetStationsAsync(CancellationToken.None);
            var status = result.Should().BeOfType<ObjectResult>().Subject;
            status.StatusCode.Should().Be(503);
        }

        private static RadioController MakeController() =>
            new RadioController(new StubUseCase(MakeResult()));

        private static RadioScheduleResultDto MakeResult()
        {
            var now = DateTimeOffset.UtcNow;
            var stations = RadioStationDefinitions.Stations
                .Select(s => new RadioStationScheduleDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    Frequency = s.Frequency,
                    Description = s.Description,
                    IsDefault = s.IsDefault,
                    CurrentSlot = new RadioHourSlotDto
                    {
                        Hour = now.Hour,
                        Mix = M($"mix-{s.Id}", s.Genres[0]),
                        IsCurrent = true,
                    },
                })
                .ToArray();

            return new RadioScheduleResultDto
            {
                GeneratedAtUtc = now,
                ScheduleDate = DateOnly.FromDateTime(now.UtcDateTime).ToString("yyyy-MM-dd"),
                Timezone = "UTC",
                CurrentHour = now.Hour,
                DefaultStationId = RadioStationDefinitions.DefaultStationId,
                Stations = stations,
            };
        }

        private static Mix M(string id, string genre) => new Mix
        {
            Id = id,
            Title = $"A - {id}",
            Url = $"https://sc.test/{id}",
            Intro = $"Intro copy for {id}.",
            Genre = genre,
            Energy = "mid",
            BpmMin = 125,
        };

        private sealed class StubUseCase : IGetRadioScheduleUseCase
        {
            private readonly RadioScheduleResultDto _r;

            internal StubUseCase(RadioScheduleResultDto r) => _r = r;

            public Task<RadioScheduleResultDto> GetAsync(CancellationToken ct) => Task.FromResult(_r);
        }

        private sealed class ThrowingUseCase : IGetRadioScheduleUseCase
        {
            public Task<RadioScheduleResultDto> GetAsync(CancellationToken ct)
                => throw new RadioStationUnavailableException("140", "No mixes found.");
        }
    }
}
