using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Infrastructure.Services.SoundCloud.Engagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Interface.Api.SoundCloud
{
    /// <summary>
    /// Composition root for the live SoundCloud play/like counts behind <c>GET /api/catalog/stats</c>.
    /// Lives in the API project, beside <c>MixLabServiceCollectionExtensions</c>, because the
    /// SoundCloud infrastructure project has no configuration, options or HTTP-factory packages.
    /// Missing credentials do not fail startup: the provider answers null and the endpoint 204.
    /// </summary>
    public static class SoundCloudEngagementServiceCollectionExtensions
    {
        public const string TokenClientName = "SoundCloudToken";
        public const string ApiClientName = "SoundCloudApi";

        private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan FetchBudget = TimeSpan.FromSeconds(6);

        public static IServiceCollection AddSoundCloudEngagement(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            SoundCloudEngagementSettings settings =
                configuration.GetSection("SoundCloud").Get<SoundCloudEngagementSettings>() ?? new SoundCloudEngagementSettings();

            services.AddHttpClient(TokenClientName, client => client.Timeout = HttpTimeout);
            services.AddHttpClient(ApiClientName, client => client.Timeout = HttpTimeout);
            services.TryAddSingleton(TimeProvider.System);

            services.AddSingleton<IMixEngagementProvider>(sp =>
            {
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

                return SoundCloudEngagementProviderFactory.Create(
                    httpClientFactory.CreateClient(TokenClientName),
                    httpClientFactory.CreateClient(ApiClientName),
                    settings,
                    sp.GetRequiredService<TimeProvider>(),
                    sp.GetRequiredService<ILoggerFactory>(),
                    FetchBudget);
            });

            return services;
        }
    }
}
