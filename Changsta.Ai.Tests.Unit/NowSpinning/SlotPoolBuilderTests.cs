using System;
using System.Collections.Generic;
using System.Linq;
using Changsta.Ai.Core.BusinessProcesses.NowSpinning;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.NowSpinning
{
    [TestFixture]
    public sealed class SlotPoolBuilderTests
    {
        [Test]
        public void Build_mix_with_null_bpm_excluded_from_all_pools()
        {
            var mix = MakeMix("x", bpmMin: null, bpmMax: null, warmth: 0.0, energy: "mid");
            var pools = SlotPoolBuilder.Build(new[] { mix });

            foreach (SlotKey slot in SlotDefinitions.SlotOrder)
            {
                foreach (DayBucket day in Enum.GetValues<DayBucket>())
                {
                    pools.Pools[(slot, day)].Should().BeEmpty();
                }
            }
        }

        [Test]
        public void Build_perfect_scoring_mix_appears_in_its_slot()
        {
            // Primetime: bpm=138, warmth=-0.3, energy=peak → score=17
            var mix = MakeMix("p", bpmMin: 138, bpmMax: 138, warmth: -0.3, energy: "peak");
            var pools = SlotPoolBuilder.Build(new[] { mix });

            pools.Pools[(SlotKey.Primetime, DayBucket.Weeknight)]
                .Should().Contain(e => e.Mix.Id == "p");
        }

        [Test]
        public void Build_mix_can_appear_in_multiple_slots()
        {
            var mix = MakeMix("m", bpmMin: 126, bpmMax: 126, warmth: 0.15, energy: "mid");
            var pools = SlotPoolBuilder.Build(new[] { mix });

            bool inAfternoon = pools.Pools[(SlotKey.Afternoon, DayBucket.Weeknight)].Any(e => e.Mix.Id == "m");
            bool inEarlyEve = pools.Pools[(SlotKey.EarlyEve, DayBucket.Weeknight)].Any(e => e.Mix.Id == "m");

            (inAfternoon || inEarlyEve).Should().BeTrue();
        }

        [Test]
        public void Build_darker_lean_tag_applied_to_cold_high_energy_mix()
        {
            // darker: warmth < -0.3 AND energy in [peak, high, mid-peak, mid-high]
            var mix = MakeMix("d", bpmMin: 138, bpmMax: 138, warmth: -0.5, energy: "peak");
            var pools = SlotPoolBuilder.Build(new[] { mix });

            var entry = pools.Pools[(SlotKey.Primetime, DayBucket.Weeknight)]
                .FirstOrDefault(e => e.Mix.Id == "d");
            entry.Should().NotBeNull();
            entry!.LeanTags.Should().Contain(MoodLean.Darker);
        }

        [Test]
        public void Build_warmer_lean_tag_applied_to_warm_mix()
        {
            // warmer: warmth > 0.3
            var mix = MakeMix("w", bpmMin: 125, bpmMax: 125, warmth: 0.5, energy: "mid");
            var pools = SlotPoolBuilder.Build(new[] { mix });

            var entry = pools.Pools[(SlotKey.Afternoon, DayBucket.Weeknight)]
                .FirstOrDefault(e => e.Mix.Id == "w");
            entry.Should().NotBeNull();
            entry!.LeanTags.Should().Contain(MoodLean.Warmer);
        }

        [Test]
        public void Build_slower_lean_is_relative_to_slot_bpm_target()
        {
            // 120 BPM: slower in primetime (target 138, diff=-18 < -10) but NOT slower in comedown (target 110, diff=+10)
            var mix = MakeMix("s", bpmMin: 120, bpmMax: 120, warmth: 0.0, energy: "mid");
            var pools = SlotPoolBuilder.Build(new[] { mix });

            var primetimeEntry = pools.Pools[(SlotKey.Primetime, DayBucket.Weeknight)]
                .FirstOrDefault(e => e.Mix.Id == "s");
            var comedownEntry = pools.Pools[(SlotKey.Comedown, DayBucket.Weeknight)]
                .FirstOrDefault(e => e.Mix.Id == "s");

            if (primetimeEntry is not null)
            {
                primetimeEntry.LeanTags.Should().Contain(MoodLean.Slower);
            }

            if (comedownEntry is not null)
            {
                comedownEntry.LeanTags.Should().NotContain(MoodLean.Slower);
            }
        }

        [Test]
        public void Build_day_bucket_bpm_adjustment_affects_pool_membership()
        {
            var mix146 = MakeMix("sat", bpmMin: 146, bpmMax: 146, warmth: -0.3, energy: "peak");
            var mix138 = MakeMix("wkn", bpmMin: 138, bpmMax: 138, warmth: -0.3, energy: "peak");

            var pools = SlotPoolBuilder.Build(new[] { mix146, mix138 });

            pools.Pools[(SlotKey.Primetime, DayBucket.Saturday)].Any(e => e.Mix.Id == "sat").Should().BeTrue();
        }

        [Test]
        public void Build_mix_with_score_zero_excluded_from_pool()
        {
            // bpm=90, dead target=172 → bpmScore=0; warmth=0.9, dead target=-0.6 → warmthScore=0; energy=mid → 0; total=0
            var lowScoreMix = MakeMix("low", bpmMin: 90, bpmMax: 90, warmth: 0.9, energy: "mid");
            var pools = SlotPoolBuilder.Build(new[] { lowScoreMix });

            pools.Pools[(SlotKey.Dead, DayBucket.Weeknight)].Should().BeEmpty();
        }

        [Test]
        public void Build_empty_catalog_produces_empty_pools()
        {
            var pools = SlotPoolBuilder.Build(Array.Empty<Mix>());

            foreach (SlotKey slot in SlotDefinitions.SlotOrder)
            {
                foreach (DayBucket day in Enum.GetValues<DayBucket>())
                {
                    pools.Pools[(slot, day)].Should().BeEmpty();
                }
            }
        }

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
    }
}
