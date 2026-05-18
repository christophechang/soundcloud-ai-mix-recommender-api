using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.NowSpinning;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;
using Changsta.Ai.Interface.Api.Controllers;
using Changsta.Ai.Interface.Api.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Changsta.Ai.Tests.Unit.Controllers
{
    [TestFixture]
    public sealed class NowSpinningProgramControllerTests
    {
        [Test]
        public async Task GetProgramAsync_includes_mix_image_url()
        {
            var result = new NowSpinningProgramResultDto
            {
                Now = new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero),
                DayBucket = "weekday",
                Slot = new NowSpinningSlotDto { Key = "day", Label = "Day" },
                Lanes = new Dictionary<string, NowSpinningProgramLaneDto>
                {
                    ["default"] = new NowSpinningProgramLaneDto
                    {
                        Mix = MakeMix("tag:soundcloud,2010:tracks/1837579572", "https://i1.sndcdn.com/artworks-test-t3000x3000.jpg"),
                    },
                },
            };

            using var cache = new MemoryCache(new MemoryCacheOptions());
            var controller = new NowSpinningProgramController(new StubNowSpinningProgramUseCase(result), cache);

            IActionResult actionResult = await controller.GetProgramAsync(cancellationToken: CancellationToken.None);

            var ok = actionResult as OkObjectResult;
            Assert.That(ok, Is.Not.Null);

            var response = ok!.Value as NowSpinningProgramResponse;
            Assert.That(response, Is.Not.Null);
            Assert.That(response!.Lanes["default"].Mix!.ImageUrl, Is.EqualTo("https://i1.sndcdn.com/artworks-test-t3000x3000.jpg"));
        }

        private static Mix MakeMix(string id, string imageUrl) => new Mix
        {
            Id = id,
            Title = "Chasing the Sun",
            Url = "https://soundcloud.com/test/chasing-the-sun",
            ImageUrl = imageUrl,
            Genre = "dnb",
            Energy = "peak",
        };

        private sealed class StubNowSpinningProgramUseCase : INowSpinningProgramUseCase
        {
            private readonly NowSpinningProgramResultDto _result;

            public StubNowSpinningProgramUseCase(NowSpinningProgramResultDto result)
            {
                _result = result;
            }

            public Task<NowSpinningProgramResultDto> GetAsync(
                NowSpinningProgramRequestDto request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(_result);
            }
        }
    }
}
