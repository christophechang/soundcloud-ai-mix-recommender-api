using System;
using Changsta.Ai.Core.BusinessProcesses.NowSpinning;
using Changsta.Ai.Core.Domain;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.NowSpinning
{
    [TestFixture]
    public sealed class SlotScorerTests
    {
        private static readonly SlotConfig Primetime =
            SlotDefinitions.Slots[SlotKey.Primetime]; // bpmTarget=138, warmth=-0.3, energy=[peak,high,mid-peak,mid-high]

        [Test]
        public void Score_perfect_bpm_gives_8_points()
        {
            var mix = MakeMix(bpmMin: 138, bpmMax: 138, warmth: -0.3, energy: "peak");
            double score = SlotScorer.Score(mix, Primetime, 138);
            score.Should().BeApproximately(8 + 4 + 5, 0.01); // 17 max
        }

        [Test]
        public void Score_bpm_48_away_from_target_gives_zero_bpm_points()
        {
            var mix = MakeMix(bpmMin: 90, bpmMax: 90, warmth: -0.3, energy: "peak");
            double bpmScore = 8 - (Math.Abs(90 - 138) / 6.0); // 8 - 8 = 0
            double score = SlotScorer.Score(mix, Primetime, 138);
            score.Should().BeGreaterThanOrEqualTo(bpmScore + 4 + 5 - 0.01);
        }

        [Test]
        public void Score_bpm_further_than_48_clamps_to_zero()
        {
            var mix = MakeMix(bpmMin: 80, bpmMax: 80, warmth: -0.3, energy: "peak");
            double score = SlotScorer.Score(mix, Primetime, 138);
            score.Should().BeLessThan(4 + 5 + 0.01); // bpm contributes 0
        }

        [Test]
        public void Score_null_bpm_returns_zero()
        {
            var mix = MakeMix(bpmMin: null, bpmMax: null, warmth: -0.3, energy: "peak");
            double score = SlotScorer.Score(mix, Primetime, 138);
            score.Should().Be(0.0);
        }

        [Test]
        public void Score_null_warmth_treated_as_zero()
        {
            var mixNullWarmth = MakeMix(bpmMin: 138, bpmMax: 138, warmth: null, energy: "peak");
            var mixZeroWarmth = MakeMix(bpmMin: 138, bpmMax: 138, warmth: 0.0, energy: "peak");
            double scoreNull = SlotScorer.Score(mixNullWarmth, Primetime, 138);
            double scoreZero = SlotScorer.Score(mixZeroWarmth, Primetime, 138);
            scoreNull.Should().BeApproximately(scoreZero, 0.001);
        }

        [Test]
        public void Score_energy_mismatch_gives_zero_energy_points()
        {
            var mix = MakeMix(bpmMin: 138, bpmMax: 138, warmth: -0.3, energy: "chilled");
            double score = SlotScorer.Score(mix, Primetime, 138);
            score.Should().BeApproximately(8 + 4, 0.01); // no energy bonus
        }

        [Test]
        public void Score_energy_match_is_exact_ordinal()
        {
            var mixExact = MakeMix(bpmMin: 138, bpmMax: 138, warmth: -0.3, energy: "peak");
            var mixWrongCase = MakeMix(bpmMin: 138, bpmMax: 138, warmth: -0.3, energy: "PEAK");
            double scoreExact = SlotScorer.Score(mixExact, Primetime, 138);
            double scoreWrong = SlotScorer.Score(mixWrongCase, Primetime, 138);
            scoreExact.Should().BeApproximately(17, 0.01);
            scoreWrong.Should().BeApproximately(8 + 4, 0.01); // no energy bonus — wrong case
        }

        [Test]
        public void Score_bpm_uses_midpoint_of_range()
        {
            // (130 + 146) / 2 = 138 → perfect score
            var mix = MakeMix(bpmMin: 130, bpmMax: 146, warmth: -0.3, energy: "peak");
            double score = SlotScorer.Score(mix, Primetime, 138);
            score.Should().BeApproximately(17, 0.01);
        }

        [Test]
        public void Score_bpm_uses_min_when_max_null()
        {
            var mix = MakeMix(bpmMin: 138, bpmMax: null, warmth: -0.3, energy: "peak");
            double score = SlotScorer.Score(mix, Primetime, 138);
            score.Should().BeApproximately(17, 0.01);
        }

        private static Mix MakeMix(int? bpmMin, int? bpmMax, double? warmth, string energy) => new Mix
        {
            Id = "test",
            Title = "Test",
            Url = "https://sc.test/test",
            Genre = "dnb",
            Energy = energy,
            BpmMin = bpmMin,
            BpmMax = bpmMax,
            Warmth = warmth,
        };
    }
}
