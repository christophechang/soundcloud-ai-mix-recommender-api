using System.Collections.Generic;
using Changsta.Ai.Infrastructure.Services.Ai.Recommenders;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Recommenders
{
    [TestFixture]
    public sealed class AiMoodWeightEnricherTests
    {
        private static readonly IReadOnlyDictionary<string, double> SampleWeights =
            new Dictionary<string, double>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["warm"] = 2.0,
                ["dark"] = -2.0,
                ["energetic"] = 1.0,
                ["neutral"] = 0.0,
            };

        [Test]
        public void BuildPrompt_includes_scale_description()
        {
            var prompt = AiMoodWeightEnricher.BuildPrompt(SampleWeights, new[] { "melancholic" });

            prompt.Should().Contain("-2.0");
            prompt.Should().Contain("2.0");
        }

        [Test]
        public void BuildPrompt_includes_all_existing_weights()
        {
            var prompt = AiMoodWeightEnricher.BuildPrompt(SampleWeights, new[] { "melancholic" });

            prompt.Should().Contain("warm");
            prompt.Should().Contain("dark");
            prompt.Should().Contain("energetic");
            prompt.Should().Contain("neutral");
        }

        [Test]
        public void BuildPrompt_includes_new_moods()
        {
            var prompt = AiMoodWeightEnricher.BuildPrompt(SampleWeights, new[] { "melancholic", "bittersweet" });

            prompt.Should().Contain("melancholic");
            prompt.Should().Contain("bittersweet");
        }

        [Test]
        public void ParseResponse_extracts_valid_mood_weight_pairs()
        {
            var json = """{ "melancholic": -0.5, "bittersweet": 0.3 }""";
            var requested = new[] { "melancholic", "bittersweet" };

            var result = AiMoodWeightEnricher.ParseResponse(json, requested);

            result.Should().ContainKey("melancholic").WhoseValue.Should().BeApproximately(-0.5, 0.001);
            result.Should().ContainKey("bittersweet").WhoseValue.Should().BeApproximately(0.3, 0.001);
        }

        [Test]
        public void ParseResponse_clamps_values_above_two()
        {
            var json = """{ "intense": 5.0 }""";
            var requested = new[] { "intense" };

            var result = AiMoodWeightEnricher.ParseResponse(json, requested);

            result["intense"].Should().Be(2.0);
        }

        [Test]
        public void ParseResponse_clamps_values_below_minus_two()
        {
            var json = """{ "brutal": -9.0 }""";
            var requested = new[] { "brutal" };

            var result = AiMoodWeightEnricher.ParseResponse(json, requested);

            result["brutal"].Should().Be(-2.0);
        }

        [Test]
        public void ParseResponse_ignores_moods_not_in_requested_list()
        {
            var json = """{ "melancholic": -0.5, "surprise_extra": 1.0 }""";
            var requested = new[] { "melancholic" };

            var result = AiMoodWeightEnricher.ParseResponse(json, requested);

            result.Should().ContainKey("melancholic");
            result.Should().NotContainKey("surprise_extra");
        }

        [Test]
        public void ParseResponse_returns_empty_on_malformed_json()
        {
            var result = AiMoodWeightEnricher.ParseResponse("not json at all", new[] { "melancholic" });

            result.Should().BeEmpty();
        }

        [Test]
        public void ParseResponse_returns_empty_on_empty_json_object()
        {
            var result = AiMoodWeightEnricher.ParseResponse("{}", new[] { "melancholic" });

            result.Should().BeEmpty();
        }

        [Test]
        public void ParseResponse_is_case_insensitive_for_mood_names()
        {
            var json = """{ "Melancholic": -0.5 }""";
            var requested = new[] { "melancholic" };

            var result = AiMoodWeightEnricher.ParseResponse(json, requested);

            result.Should().ContainKey("Melancholic");
        }
    }
}
