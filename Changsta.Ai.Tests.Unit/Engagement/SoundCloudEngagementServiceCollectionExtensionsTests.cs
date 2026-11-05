using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Interface.Api.SoundCloud;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Engagement
{
    [TestFixture]
    public sealed class SoundCloudEngagementServiceCollectionExtensionsTests
    {
        [Test]
        public async Task AddSoundCloudEngagement_without_credentials_resolves_a_provider_that_returns_null()
        {
            using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
            {
                ["SoundCloud:RssUrl"] = "https://feeds.soundcloud.com/users/soundcloud:users:123456/sounds.rss",
                ["SoundCloud:ClientId"] = string.Empty,
                ["SoundCloud:ClientSecret"] = string.Empty,
            });

            IMixEngagementProvider sut = provider.GetRequiredService<IMixEngagementProvider>();

            (await sut.GetAllAsync(CancellationToken.None)).Should().BeNull();
        }

        [Test]
        public void AddSoundCloudEngagement_registers_the_provider_as_a_singleton()
        {
            using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>());

            provider.GetRequiredService<IMixEngagementProvider>()
                .Should().BeSameAs(provider.GetRequiredService<IMixEngagementProvider>());
        }

        private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
        {
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSoundCloudEngagement(configuration);
            return services.BuildServiceProvider();
        }
    }
}
