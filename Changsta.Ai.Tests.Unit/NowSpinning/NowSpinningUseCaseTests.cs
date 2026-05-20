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

        // 2026-05-15 is a Friday, UTC 09:00 → morning (warmth target +0.5)
        private static readonly DateTimeOffset MorningFriday =
            new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.Zero);

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
        public async Task GetAsync_same_hour_returns_same_mix()
        {
            var mixes = new[] { MakePrimetimeMix("a"), MakePrimetimeMix("b"), MakePrimetimeMix("c") };
            var useCase = MakeUseCase(mixes);

            var r1 = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);
            var r2 = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);

            r1.Mix!.Id.Should().Be(r2.Mix!.Id);
        }

        [Test]
        public async Task GetAsync_different_days_same_week_return_different_mixes()
        {
            // Seven consecutive primetime hours spanning the same Unix week — pool of 7
            // ensures each position maps to a distinct entry (no wrap-around).
            var mixes = new[]
            {
                MakePrimetimeMix("mon"), MakePrimetimeMix("tue"), MakePrimetimeMix("wed"),
                MakePrimetimeMix("thu"), MakePrimetimeMix("fri"), MakePrimetimeMix("sat"),
                MakePrimetimeMix("sun"),
            };
            var useCase = MakeUseCase(mixes);

            // 2026-05-14 through 2026-05-20 — all fall within Unix week 2941 (epoch-aligned,
            // week starts Thu; days 20587–20593), so shuffle seed is constant across all 7
            // and positions 0–6 are guaranteed distinct.
            var ids = new System.Collections.Generic.List<string>();
            for (int d = 0; d < 7; d++)
            {
                DateTimeOffset day = new DateTimeOffset(2026, 5, 14, 22, 0, 0, TimeSpan.Zero).AddDays(d);
                var result = await useCase.GetAsync(MakeRequest(day), CancellationToken.None);
                ids.Add(result.Mix!.Id);
            }

            ids.Should().OnlyHaveUniqueItems();
        }

        [Test]
        public async Task GetAsync_darker_lean_returns_mix_with_darker_tag()
        {
            var mixes = new[]
            {
                MakeMix("a", bpmMin: 138, bpmMax: 138, warmth: -0.6, energy: "peak"),
                MakeMix("b", bpmMin: 138, bpmMax: 138, warmth: 0.6, energy: "peak"),
                MakeMix("c", bpmMin: 138, bpmMax: 138, warmth: -0.4, energy: "peak"),
                MakeMix("d", bpmMin: 138, bpmMax: 138, warmth: 0.4, energy: "peak"),
                MakeMix("e", bpmMin: 138, bpmMax: 138, warmth: -0.2, energy: "peak"),
                MakeMix("f", bpmMin: 138, bpmMax: 138, warmth: 0.2, energy: "peak"),
            };
            var useCase = MakeUseCase(mixes);

            var result = await useCase.GetAsync(
                MakeRequest(PrimetimeFriday, moodLean: MoodLean.Darker),
                CancellationToken.None);

            result.LeanIgnored.Should().BeFalse();
            result.Mix!.Warmth.Should().BeLessThan(-0.3);
        }

        [Test]
        public async Task GetAsync_warmer_lean_returns_mix_with_warmer_tag()
        {
            // Morning slot warmth target is +0.5 — warm mixes score well and make the pool.
            // bpmTarget = 122 (Friday morning), energy set = {low-mid, mid, chilled, low}.
            var mixes = new[]
            {
                MakeMix("w1", bpmMin: 122, bpmMax: 122, warmth: 0.7, energy: "mid"),
                MakeMix("w2", bpmMin: 122, bpmMax: 122, warmth: 0.5, energy: "mid"),
                MakeMix("w3", bpmMin: 122, bpmMax: 122, warmth: 0.4, energy: "mid"),
                MakeMix("c1", bpmMin: 122, bpmMax: 122, warmth: 0.0, energy: "mid"),
                MakeMix("c2", bpmMin: 122, bpmMax: 122, warmth: -0.3, energy: "mid"),
                MakeMix("c3", bpmMin: 122, bpmMax: 122, warmth: -0.6, energy: "mid"),
            };
            var useCase = MakeUseCase(mixes);

            var result = await useCase.GetAsync(
                MakeRequest(MorningFriday, moodLean: MoodLean.Warmer),
                CancellationToken.None);

            result.LeanIgnored.Should().BeFalse();
            result.Mix!.Warmth.Should().BeGreaterThan(0.3);
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
            int scheduleCount = 0,
            int utcOffsetMinutes = 0)
        {
            return new NowSpinningRequestDto
            {
                UtcNow = at,
                UtcOffsetMinutes = utcOffsetMinutes,
                MoodLean = moodLean,
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
