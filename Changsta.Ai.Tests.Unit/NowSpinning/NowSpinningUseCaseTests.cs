using System;
using System.Collections.Generic;
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
    public sealed class NowSpinningUseCaseTests
    {
        // 2026-05-15 is a Friday, UTC 22:00 → primetime
        private static readonly DateTimeOffset PrimetimeFriday =
            new DateTimeOffset(2026, 5, 15, 22, 0, 0, TimeSpan.Zero);

        [Test]
        public async Task GetAsync_returns_mix_from_matching_slot()
        {
            var mix = MakePrimetimeMix("pt1");
            var useCase = MakeUseCase(new[] { mix });

            var result = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);

            result.Mix.Should().NotBeNull();
            result.Mix!.Id.Should().Be("pt1");
            result.Slot.Key.Should().Be("primetime");
            result.DayBucket.Should().Be("friday");
        }

        [Test]
        public async Task GetAsync_same_hour_same_skip_state_returns_same_mix()
        {
            var mixes = new[] { MakePrimetimeMix("a"), MakePrimetimeMix("b"), MakePrimetimeMix("c") };
            var useCase = MakeUseCase(mixes);

            var r1 = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);
            var r2 = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);

            r1.Mix!.Id.Should().Be(r2.Mix!.Id);
        }

        [Test]
        public async Task GetAsync_different_skip_state_may_return_different_mix()
        {
            var mixes = new[] { MakePrimetimeMix("a"), MakePrimetimeMix("b"), MakePrimetimeMix("c") };
            var useCase = MakeUseCase(mixes);

            var r1 = await useCase.GetAsync(MakeRequest(PrimetimeFriday, skipIds: Array.Empty<string>()), CancellationToken.None);
            var r2 = await useCase.GetAsync(MakeRequest(PrimetimeFriday, skipIds: new[] { r1.Mix!.Id }), CancellationToken.None);

            r2.Mix!.Id.Should().NotBe(r1.Mix.Id);
        }

        [Test]
        public async Task GetAsync_skip_exhausts_pool_sets_skipsIgnored()
        {
            var mix = MakePrimetimeMix("only");
            var useCase = MakeUseCase(new[] { mix });

            var result = await useCase.GetAsync(
                MakeRequest(PrimetimeFriday, skipIds: new[] { "only" }),
                CancellationToken.None);

            result.SkipsIgnored.Should().BeTrue();
            result.Mix.Should().NotBeNull();
        }

        [Test]
        public async Task GetAsync_lean_exhausts_pool_sets_leanIgnored()
        {
            // Mix is warm (warmth=0.5) → tagged warmer, not darker
            var mix = MakeMix("warm", bpmMin: 138, bpmMax: 138, warmth: 0.5, energy: "peak");
            var useCase = MakeUseCase(new[] { mix });

            var result = await useCase.GetAsync(
                MakeRequest(PrimetimeFriday, moodLean: MoodLean.Darker),
                CancellationToken.None);

            result.LeanIgnored.Should().BeTrue();
            result.SkipsIgnored.Should().BeFalse();
            result.Mix.Should().NotBeNull();
        }

        [Test]
        public async Task GetAsync_schedule_excludes_now_mix()
        {
            var mixes = new[]
            {
                MakePrimetimeMix("a"),
                MakePrimetimeMix("b"),
                MakePrimetimeMix("c"),
                MakePrimetimeMix("d"),
                MakePrimetimeMix("e"),
            };
            var useCase = MakeUseCase(mixes);

            var result = await useCase.GetAsync(
                MakeRequest(PrimetimeFriday, scheduleCount: 4),
                CancellationToken.None);

            string nowId = result.Mix!.Id;
            result.Schedule.Should().NotContain(e => e.Mix != null && e.Mix.Id == nowId);
        }

        [Test]
        public async Task GetAsync_schedule_has_correct_count()
        {
            var mixes = new[]
            {
                MakePrimetimeMix("a"), MakePrimetimeMix("b"), MakePrimetimeMix("c"),
                MakePrimetimeMix("d"), MakePrimetimeMix("e"),
            };
            var useCase = MakeUseCase(mixes);

            var result = await useCase.GetAsync(
                MakeRequest(PrimetimeFriday, scheduleCount: 4),
                CancellationToken.None);

            result.Schedule.Should().HaveCount(4);
        }

        [Test]
        public async Task GetAsync_schedule_at_values_are_floored_to_hour()
        {
            var atWithMinutes = new DateTimeOffset(2026, 5, 15, 22, 37, 0, TimeSpan.Zero);
            var useCase = MakeUseCase(new[] { MakePrimetimeMix("x"), MakePrimetimeMix("y"), MakePrimetimeMix("z") });

            var result = await useCase.GetAsync(
                MakeRequest(atWithMinutes, scheduleCount: 2),
                CancellationToken.None);

            result.Schedule[0].At.Minute.Should().Be(0);
            result.Schedule[0].At.Second.Should().Be(0);
            result.Schedule[0].At.Hour.Should().Be(23);
        }

        [Test]
        public async Task GetAsync_empty_pool_sets_noMixAvailable()
        {
            var useCase = MakeUseCase(Array.Empty<Mix>());

            var result = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);

            result.NoMixAvailable.Should().BeTrue();
            result.Mix.Should().BeNull();
        }

        [Test]
        public async Task GetAsync_utcOffsetMinutes_shifts_local_hour()
        {
            var deadMix = MakeMix("dead1", bpmMin: 172, bpmMax: 172, warmth: -0.6, energy: "peak");
            var useCase = MakeUseCase(new[] { deadMix });
            var utcOne = new DateTimeOffset(2026, 5, 18, 1, 0, 0, TimeSpan.Zero); // Monday

            var resultDead = await useCase.GetAsync(
                new NowSpinningRequestDto { UtcNow = utcOne, UtcOffsetMinutes = 0, ScheduleCount = 0 },
                CancellationToken.None);

            resultDead.Slot.Key.Should().Be("dead");
        }

        [Test]
        public async Task GetAsync_now_field_reflects_utcNow_not_local()
        {
            var useCase = MakeUseCase(new[] { MakePrimetimeMix("x") });
            var result = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);
            result.Now.Should().Be(PrimetimeFriday);
        }

        private static NowSpinningUseCase MakeUseCase(IReadOnlyList<Mix> mixes)
        {
            return new NowSpinningUseCase(new StubCatalogueProvider(mixes));
        }

        private static NowSpinningRequestDto MakeRequest(
            DateTimeOffset at,
            MoodLean? moodLean = null,
            string[]? skipIds = null,
            int scheduleCount = 0,
            int utcOffsetMinutes = 0)
        {
            return new NowSpinningRequestDto
            {
                UtcNow = at,
                UtcOffsetMinutes = utcOffsetMinutes,
                MoodLean = moodLean,
                SkipIds = skipIds ?? Array.Empty<string>(),
                ScheduleCount = scheduleCount,
            };
        }

        private static Mix MakePrimetimeMix(string id) =>
            MakeMix(id, bpmMin: 138, bpmMax: 138, warmth: -0.3, energy: "peak");

        private static Mix MakeMix(string id, int? bpmMin, int? bpmMax, double? warmth, string energy) => new Mix
        {
            Id = id,
            Title = $"Mix {id}",
            Url = $"https://sc.test/{id}",
            Genre = "dnb",
            Energy = energy,
            BpmMin = bpmMin,
            BpmMax = bpmMax,
            Warmth = warmth,
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
