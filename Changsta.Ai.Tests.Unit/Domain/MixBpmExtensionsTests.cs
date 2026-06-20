using Changsta.Ai.Core.Domain;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Domain
{
    [TestFixture]
    public sealed class MixBpmExtensionsTests
    {
        [TestCase(120, 124, 122)]
        [TestCase(121, 126, 124)] // 123.5 rounds to even -> 124
        [TestCase(100, 100, 100)]
        public void GetMidBpm_returns_rounded_average_when_both_bounds_present(int min, int max, int expected)
        {
            Mix mix = MakeMix(min, max);

            mix.GetMidBpm().Should().Be(expected);
        }

        [Test]
        public void GetMidBpm_returns_single_bound_when_only_one_present()
        {
            MakeMix(min: 128, max: null).GetMidBpm().Should().Be(128);
            MakeMix(min: null, max: 140).GetMidBpm().Should().Be(140);
            MakeMix(min: null, max: null).GetMidBpm().Should().BeNull();
        }

        private static Mix MakeMix(int? min, int? max) => new Mix
        {
            Id = "m",
            Title = "t",
            Url = "https://sc.test/m",
            Genre = "house",
            Energy = "mid",
            BpmMin = min,
            BpmMax = max,
        };
    }
}
