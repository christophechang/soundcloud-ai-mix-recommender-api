using Changsta.Ai.Core.Contracts.Catalogue;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Engagement
{
    /// <summary>
    /// Composes the internal token cache, engagement client and cached provider from plain
    /// arguments, so the API project can wire them without this project taking DI, options or
    /// HTTP-factory packages, and without the three classes becoming public.
    /// </summary>
    public static class SoundCloudEngagementProviderFactory
    {
        public static IMixEngagementProvider Create(
            HttpClient tokenHttpClient,
            HttpClient apiHttpClient,
            SoundCloudEngagementSettings settings,
            TimeProvider timeProvider,
            ILoggerFactory loggerFactory,
            TimeSpan fetchBudget)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);

            var tokenCache = new SoundCloudAppTokenCache(
                tokenHttpClient,
                settings,
                timeProvider,
                loggerFactory.CreateLogger<SoundCloudAppTokenCache>());

            var client = new SoundCloudEngagementClient(
                apiHttpClient,
                tokenCache,
                loggerFactory.CreateLogger<SoundCloudEngagementClient>());

            return new CachedMixEngagementProvider(client, settings, timeProvider, fetchBudget);
        }
    }
}
