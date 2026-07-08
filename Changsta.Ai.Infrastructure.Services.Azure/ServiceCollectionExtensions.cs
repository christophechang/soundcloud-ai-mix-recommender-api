using System;
using Azure.Core;
using Azure.Identity;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Contracts.Diagnostics;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Infrastructure.Services.Azure.Catalogue;
using Changsta.Ai.Infrastructure.Services.Azure.Configuration;
using Changsta.Ai.Infrastructure.Services.Azure.Diagnostics;
using Changsta.Ai.Infrastructure.Services.Azure.MixLab;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
            AddAzureCredential(services);

            // Repositories hold a long-lived BlobContainerClient (thread-safe per Azure SDK
            // guidance) and no per-request state, so they register as singletons to avoid
            // rebuilding the container client + credential per HTTP request.
            services.AddSingleton<IBlobMixCatalogueRepository, BlobMixCatalogueRepository>();
            services.AddSingleton<IMoodWeightEnrichmentRepository, BlobMoodWeightEnrichmentRepository>();
            services.AddScoped<Changsta.Ai.Core.Contracts.Catalogue.ICatalogMixDeleter, BlobCatalogMixDeleter>();

            return services;
        }

        public static IServiceCollection AddMixLabAzureServices(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddSingleton<IValidateOptions<MixLabStorageOptions>, MixLabStorageOptionsValidator>();
            services.AddOptions<MixLabStorageOptions>()
                .Bind(configuration.GetSection("Azure:MixLab"))
                .ValidateOnStart();
            AddAzureCredential(services);

            // TimeProvider.System is the default clock; MixLab's claim-lease logic (see
            // BlobMixLabRunRepository.TryClaimOldestQueuedAsync) takes TimeProvider as a
            // constructor dependency so it can be swapped for a fake in tests — this repo has no
            // pre-existing time abstraction to reuse (issue #128).
            services.TryAddSingleton(TimeProvider.System);

            // Repositories hold a long-lived BlobContainerClient (thread-safe per Azure SDK
            // guidance) and no per-request state, so they register as singletons — mirrors
            // AddAzureBlobMixCatalog above.
            services.AddSingleton<IMixLabBlobGateway, MixLabBlobGateway>();
            services.AddSingleton<IMixLabRunRepository, BlobMixLabRunRepository>();
            services.AddSingleton<IMixLabUploadRepository, BlobMixLabUploadRepository>();
            services.AddSingleton<IMixLabArtifactStore, BlobMixLabArtifactStore>();
            services.AddSingleton<IMixLabHistoryStore, BlobMixLabHistoryStore>();
            services.AddSingleton<IMixLabFeedbackQueue, BlobMixLabFeedbackQueue>();

            return services;
        }

        public static IServiceCollection AddAzureDiagnostics(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // LogAnalytics WorkspaceId is intentionally optional: an empty value disables the
            // diagnostics path (see AppInsightsDiagnosticsProvider). No ValidateOnStart here.
            services.Configure<LogAnalyticsOptions>(configuration.GetSection("Azure:LogAnalytics"));
            AddAzureCredential(services);

            // AppInsightsDiagnosticsProvider holds a long-lived LogsQueryClient; the underlying
            // HttpPipeline + token cache are designed to be reused across requests.
            services.AddSingleton<IErrorInsightsProvider, AppInsightsDiagnosticsProvider>();

            return services;
        }

        private static void AddAzureCredential(IServiceCollection services)
        {
            // DefaultAzureCredential caches token acquisitions internally. Construct it once per
            // process so MSI / AAD lookups are not repeated on every dependent request.
            services.TryAddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
        }
    }
}
