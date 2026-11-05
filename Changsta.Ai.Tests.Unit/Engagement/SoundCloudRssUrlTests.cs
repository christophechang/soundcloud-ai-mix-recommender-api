using Changsta.Ai.Infrastructure.Services.SoundCloud.Engagement;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Engagement
{
    [TestFixture]
    public sealed class SoundCloudRssUrlTests
    {
        [Test]
        public void TryParseUserId_reads_id_from_feed_url()
        {
            bool parsed = SoundCloudRssUrl.TryParseUserId(
                "https://feeds.soundcloud.com/users/soundcloud:users:123456/sounds.rss",
                out long userId);

            parsed.Should().BeTrue();
            userId.Should().Be(123456);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("https://feeds.soundcloud.com/users/changsta/sounds.rss")]
        [TestCase("https://feeds.soundcloud.com/users/soundcloud:users:abc/sounds.rss")]
        [TestCase("https://feeds.soundcloud.com/users/soundcloud:users:/sounds.rss")]
        [TestCase("https://feeds.soundcloud.com/users/soundcloud:users:0/sounds.rss")]
        public void TryParseUserId_rejects_blank_malformed_and_non_numeric(string? rssUrl)
        {
            bool parsed = SoundCloudRssUrl.TryParseUserId(rssUrl, out long userId);

            parsed.Should().BeFalse();
            userId.Should().Be(0);
        }
    }
}
