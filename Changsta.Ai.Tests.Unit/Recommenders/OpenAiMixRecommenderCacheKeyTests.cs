using System;
using Changsta.Ai.Infrastructure.Services.Ai.Recommenders;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Recommenders
{
    [TestFixture]
    public sealed class OpenAiMixRecommenderCacheKeyTests
    {
        [Test]
        public void ResolveCacheTtl_uses_short_ttl_for_empty_results()
        {
            TimeSpan emptyTtl = OpenAiMixRecommender.ResolveCacheTtl(0);
            TimeSpan matchTtl = OpenAiMixRecommender.ResolveCacheTtl(3);

            emptyTtl.Should().Be(TimeSpan.FromMinutes(5));
            matchTtl.Should().Be(TimeSpan.FromMinutes(60));
            emptyTtl.Should().BeLessThan(matchTtl);
        }

        [Test]
        public void BuildCacheKey_includes_prompt_and_catalogue_version()
        {
            string key = OpenAiMixRecommender.BuildCacheKey("warm sunday set", 5, promptVersion: 3, catalogueVersion: 7);

            key.Should().Be("recommend:v3:c7:warm sunday set:5");
        }

        [Test]
        public void BuildCacheKey_changes_when_catalogue_version_changes()
        {
            string before = OpenAiMixRecommender.BuildCacheKey("warm sunday set", 5, promptVersion: 1, catalogueVersion: 4);
            string after = OpenAiMixRecommender.BuildCacheKey("warm sunday set", 5, promptVersion: 1, catalogueVersion: 5);

            after.Should().NotBe(before);
        }

        [Test]
        public void BuildCacheKey_changes_when_prompt_version_changes()
        {
            string before = OpenAiMixRecommender.BuildCacheKey("warm sunday set", 5, promptVersion: 1, catalogueVersion: 4);
            string after = OpenAiMixRecommender.BuildCacheKey("warm sunday set", 5, promptVersion: 2, catalogueVersion: 4);

            after.Should().NotBe(before);
        }

        [Test]
        public void BuildCacheKey_normalises_question_casing_and_whitespace()
        {
            string padded = OpenAiMixRecommender.BuildCacheKey("  Warm Sunday Set  ", 5, promptVersion: 1, catalogueVersion: 0);
            string canonical = OpenAiMixRecommender.BuildCacheKey("warm sunday set", 5, promptVersion: 1, catalogueVersion: 0);

            padded.Should().Be(canonical);
        }

        [Test]
        public void BuildCacheKey_differs_by_max_results()
        {
            string five = OpenAiMixRecommender.BuildCacheKey("warm sunday set", 5, promptVersion: 1, catalogueVersion: 0);
            string ten = OpenAiMixRecommender.BuildCacheKey("warm sunday set", 10, promptVersion: 1, catalogueVersion: 0);

            ten.Should().NotBe(five);
        }
    }
}
