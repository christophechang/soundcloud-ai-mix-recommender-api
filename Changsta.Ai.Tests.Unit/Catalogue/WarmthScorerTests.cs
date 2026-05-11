using System;
using System.Collections.Generic;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.Azure.Catalogue;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Catalogue
{
    [TestFixture]
    public sealed class WarmthScorerTests
    {
        private static readonly IReadOnlyDictionary<string, double> Weights =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["dark"] = -2.0,
                ["warm"] = 2.0,
                ["groovy"] = 1.2,
                ["driving"] = -1.0,
                ["rhythmic"] = 0.0,
            };

        [Test]
        public void ComputeWarmth_single_positive_mood_mid_energy_scores_positive()
        {
            var mix = MakeMix("1", moods: new[] { "groovy" }, energy: "mid");
            var result = WarmthScorer.ComputeWarmth(new[] { mix }, Weights, NullLogger.Instance, out _);
            result[0].Warmth.Should().BeApproximately(1.2 / 1.8, 0.0001);
        }

        [Test]
        public void ComputeWarmth_single_negative_mood_mid_energy_scores_negative()
        {
            var mix = MakeMix("1", moods: new[] { "driving" }, energy: "mid");
            var result = WarmthScorer.ComputeWarmth(new[] { mix }, Weights, NullLogger.Instance, out _);
            result[0].Warmth.Should().BeApproximately(-1.0 / 1.8, 0.0001);
        }

        [Test]
        public void ComputeWarmth_score_clamped_to_positive_one()
        {
            var weights = new Dictionary<string, double> { ["warm"] = 2.0 };
            var mix = MakeMix("1", moods: new[] { "warm" }, energy: "low");
            var result = WarmthScorer.ComputeWarmth(new[] { mix }, weights, NullLogger.Instance, out _);
            result[0].Warmth.Should().Be(1.0);
        }

        [Test]
        public void ComputeWarmth_score_clamped_to_negative_one()
        {
            var weights = new Dictionary<string, double> { ["dark"] = -2.0 };
            var mix = MakeMix("1", moods: new[] { "dark" }, energy: "high");
            var result = WarmthScorer.ComputeWarmth(new[] { mix }, weights, NullLogger.Instance, out _);
            result[0].Warmth.Should().Be(-1.0);
        }

        [Test]
        public void ComputeWarmth_high_energy_nudge_shifts_score_colder()
        {
            var mix = MakeMix("1", moods: new[] { "groovy" }, energy: "high");
            var mixNoNudge = MakeMix("2", moods: new[] { "groovy" }, energy: "mid");

            var results = WarmthScorer.ComputeWarmth(new[] { mix, mixNoNudge }, Weights, NullLogger.Instance, out _);

            results[0].Warmth.Should().BeLessThan(results[1].Warmth!.Value);
        }

        [Test]
        public void ComputeWarmth_low_energy_nudge_shifts_score_warmer()
        {
            var mix = MakeMix("1", moods: new[] { "driving" }, energy: "low");
            var mixNoNudge = MakeMix("2", moods: new[] { "driving" }, energy: "mid");

            var results = WarmthScorer.ComputeWarmth(new[] { mix, mixNoNudge }, Weights, NullLogger.Instance, out _);

            results[0].Warmth.Should().BeGreaterThan(results[1].Warmth!.Value);
        }

        [Test]
        public void ComputeWarmth_zero_weight_mood_excluded_from_average()
        {
            var withNeutral = MakeMix("1", moods: new[] { "dark", "rhythmic" }, energy: "mid");
            var withoutNeutral = MakeMix("2", moods: new[] { "dark" }, energy: "mid");

            var results = WarmthScorer.ComputeWarmth(
                new[] { withNeutral, withoutNeutral }, Weights, NullLogger.Instance, out _);

            results[0].Warmth.Should().Be(results[1].Warmth);
        }

        [Test]
        public void ComputeWarmth_no_moods_returns_null_warmth()
        {
            var mix = MakeMix("1", moods: Array.Empty<string>(), energy: "mid");
            var result = WarmthScorer.ComputeWarmth(new[] { mix }, Weights, NullLogger.Instance, out _);
            result[0].Warmth.Should().BeNull();
        }

        [Test]
        public void ComputeWarmth_all_zero_weight_moods_returns_null_warmth()
        {
            var mix = MakeMix("1", moods: new[] { "rhythmic" }, energy: "mid");
            var result = WarmthScorer.ComputeWarmth(new[] { mix }, Weights, NullLogger.Instance, out _);
            result[0].Warmth.Should().BeNull();
        }

        [Test]
        public void ComputeWarmth_mixed_moods_averaged_not_summed()
        {
            var twoMoods = MakeMix("1", moods: new[] { "warm", "dark" }, energy: "mid");
            var result = WarmthScorer.ComputeWarmth(new[] { twoMoods }, Weights, NullLogger.Instance, out _);
            result[0].Warmth.Should().BeApproximately(0.0, 0.0001);
        }

        [Test]
        public void ComputeWarmth_changed_false_when_warmth_already_correct()
        {
            var mix = MakeMix("1", moods: new[] { "warm" }, energy: "mid");
            var firstPass = WarmthScorer.ComputeWarmth(new[] { mix }, Weights, NullLogger.Instance, out bool firstChanged);
            firstChanged.Should().BeTrue();

            WarmthScorer.ComputeWarmth(firstPass, Weights, NullLogger.Instance, out bool secondChanged);
            secondChanged.Should().BeFalse();
        }

        [Test]
        public void ComputeWarmth_changed_true_when_warmth_was_null()
        {
            var mix = MakeMix("1", moods: new[] { "warm" }, energy: "mid");
            WarmthScorer.ComputeWarmth(new[] { mix }, Weights, NullLogger.Instance, out bool changed);
            changed.Should().BeTrue();
        }

        [Test]
        public void ComputeWarmth_mood_lookup_case_insensitive()
        {
            var mixLower = MakeMix("1", moods: new[] { "warm" }, energy: "mid");
            var mixUpper = MakeMix("2", moods: new[] { "WARM" }, energy: "mid");

            var results = WarmthScorer.ComputeWarmth(
                new[] { mixLower, mixUpper }, Weights, NullLogger.Instance, out _);

            results[0].Warmth.Should().Be(results[1].Warmth);
        }

        private static Mix MakeMix(
            string id,
            IReadOnlyList<string>? moods = null,
            string energy = "mid")
        {
            return new Mix
            {
                Id = id,
                Title = "Test Mix",
                Url = $"https://sc.test/{id}",
                Genre = "dnb",
                Energy = energy,
                Moods = moods ?? Array.Empty<string>(),
            };
        }
    }
}
