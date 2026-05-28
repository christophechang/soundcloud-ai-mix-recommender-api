using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Contracts.Diagnostics;
using Changsta.Ai.Infrastructure.Services.Azure.Catalogue;
using Changsta.Ai.Infrastructure.Services.Azure.Configuration;
using Changsta.Ai.Infrastructure.Services.Azure.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Changsta.Ai.Infrastructure.Services.Azure
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAzureBlobMixCatalog(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddSingleton<IValidateOptions<BlobCatalogOptions>, BlobCatalogOptionsValidator>();
            services.AddOptions<BlobCatalogOptions>()
                .Bind(configuration.GetSection("Azure:BlobCatalog"))
                .ValidateOnStart();

            services.AddScoped<IBlobMixCatalogueRepository, BlobMixCatalogueRepository>();
            services.AddScoped<IMoodWeightEnrichmentRepository, BlobMoodWeightEnrichmentRepository>();
            services.AddScoped<Changsta.Ai.Core.Contracts.Catalogue.ICatalogMixDeleter, BlobCatalogMixDeleter>();

            return services;
        }

        public static IServiceCollection AddAzureDiagnostics(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // LogAnalytics WorkspaceId is intentionally optional: an empty value disables the
            // diagnostics path (see AppInsightsDiagnosticsProvider). No ValidateOnStart here.
            services.Configure<LogAnalyticsOptions>(configuration.GetSection("Azure:LogAnalytics"));

            services.AddScoped<IErrorInsightsProvider, AppInsightsDiagnosticsProvider>();

            return services;
        }
    }
}
