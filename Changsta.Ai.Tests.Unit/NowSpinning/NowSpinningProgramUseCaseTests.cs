using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.BusinessProcesses.NowSpinning;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.NowSpinning
{
    [TestFixture]
    public sealed class NowSpinningProgramUseCaseTests
    {
        // 2026-05-15 is a Friday, UTC 22:00 → primetime
        private static readonly DateTimeOffset PrimetimeFriday =
            new DateTimeOffset(2026, 5, 15, 22, 0, 0, TimeSpan.Zero);

        [Test]
        public async Task GetAsync_all_five_lanes_present()
        {
            var mixes = MakePrimetimeMixes("a", "b", "c", "d", "e", "f");
            var useCase = MakeUseCase(mixes);

            var result = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);

            result.Lanes.Keys.Should().BeEquivalentTo(new[] { "default", "darker", "warmer", "slower", "faster" });
        }

        [Test]
        public async Task GetAsync_now_picks_are_unique_across_lanes()
        {
            var mixes = MakePrimetimeMixes("a", "b", "c", "d", "e", "f", "g", "h");
            var useCase = MakeUseCase(mixes);

            var result = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);

            var nowIds = result.Lanes.Values
                .Where(l => l.Mix is not null)
                .Select(l => l.Mix!.Id)
                .ToList();

            nowIds.Should().OnlyHaveUniqueItems();
        }

        [Test]
        public async Task GetAsync_same_request_returns_same_result()
        {
            var mixes = MakePrimetimeMixes("a", "b", "c", "d", "e", "f");
            var useCase = MakeUseCase(mixes);

            var r1 = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);
            var r2 = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);

            r1.Lanes["default"].Mix!.Id.Should().Be(r2.Lanes["default"].Mix!.Id);
            r1.Lanes["darker"].Mix!.Id.Should().Be(r2.Lanes["darker"].Mix!.Id);
        }

        [Test]
        public async Task GetAsync_schedule_has_correct_count()
        {
            var mixes = MakePrimetimeMixes("a", "b", "c", "d", "e", "f", "g", "h", "i", "j");
            var useCase = MakeUseCase(mixes);

            var result = await useCase.GetAsync(MakeRequest(PrimetimeFriday, scheduleCount: 3), CancellationToken.None);

            result.Lanes["default"].Schedule.Should().HaveCount(3);
        }

        [Test]
        public async Task GetAsync_now_field_reflects_utcNow()
        {
            var mixes = MakePrimetimeMixes("a", "b", "c", "d", "e");
            var useCase = MakeUseCase(mixes);

            var result = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);

            result.Now.Should().Be(PrimetimeFriday);
        }

        private static NowSpinningProgramUseCase MakeUseCase(IReadOnlyList<Mix> mixes)
            => new NowSpinningProgramUseCase(new StubCatalogueProvider(mixes));

        private static NowSpinningProgramRequestDto MakeRequest(
            DateTimeOffset at,
            int scheduleCount = 0,
            int utcOffsetMinutes = 0)
        {
            return new NowSpinningProgramRequestDto
            {
                UtcNow = at,
                UtcOffsetMinutes = utcOffsetMinutes,
                ScheduleCount = scheduleCount,
            };
        }

        private static IReadOnlyList<Mix> MakePrimetimeMixes(params string[] ids)
            => ids.Select(id => MakeMix(id)).ToArray();

        private static Mix MakeMix(string id) => new Mix
        {
            Id = id,
            Title = $"Mix {id}",
            Url = $"https://sc.test/{id}",
            Genre = "dnb",
            Energy = "peak",
            BpmMin = 138,
            BpmMax = 138,
            Warmth = -0.3,
        };

        private sealed class StubCatalogueProvider : IMixCatalogueProvider
        {
            private readonly IReadOnlyList<Mix> _mixes;

            public StubCatalogueProvider(IReadOnlyList<Mix> mixes) => _mixes = mixes;

            public Task<IReadOnlyList<Mix>> GetLatestAsync(int maxItems, CancellationToken cancellationToken)
                => Task.FromResult(_mixes);
        }
    }
}
