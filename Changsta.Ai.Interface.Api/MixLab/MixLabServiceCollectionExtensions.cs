using Changsta.Ai.Core.BusinessProcesses.MixLab;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Infrastructure.Services.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Changsta.Ai.Interface.Api.MixLab
{
    /// <summary>
    /// Composition root for MixLab Anywhere (A5): wires A1's blob-backed repositories via
    /// <see cref="ServiceCollectionExtensions.AddMixLabAzureServices"/>, binds
    /// <see cref="MixLabOptions"/> from the <c>MixLab</c> configuration section, and registers the
    /// A2 upload and A3 run-queue use cases. This composition lives in the API project (rather than
    /// alongside the use cases in Core.BusinessProcesses, cf.
    /// <c>Changsta.Ai.Core.BusinessProcesses.Radio.RadioServiceCollectionExtensions</c>) because it
    /// must reference the Infrastructure.Services.Azure project, which Core.BusinessProcesses does
    /// not and must not depend on. See issue #132.
    /// </summary>
    public static class MixLabServiceCollectionExtensions
    {
        public static IServiceCollection AddMixLabServices(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddMixLabAzureServices(configuration);

            // MixLabOptions is a plain constructor-injected POCO, not IOptions<T>, by design (see
            // its docstring — issue #130 predates this ticket binding it). Bind it once here from
            // the "MixLab" section and register the resulting instance as a singleton.
            MixLabOptions options = configuration.GetSection("MixLab").Get<MixLabOptions>() ?? new MixLabOptions();
            services.AddSingleton(options);

            // Upload use cases (A2). Scoped to match the other controller-facing use cases
            // registered in Program.cs (e.g. ICatalogFlushUseCase, IMixRecommendationUseCase).
            services.AddScoped<IUploadCollectionUseCase, UploadCollectionUseCase>();
            services.AddScoped<IGetUploadsUseCase, GetUploadsUseCase>();
            services.AddScoped<IOpenUploadUseCase, OpenUploadUseCase>();

            // Run-queue use cases (A3).
            services.AddScoped<IEnqueueMixLabRunUseCase, EnqueueMixLabRunUseCase>();
            services.AddScoped<IClaimMixLabRunUseCase, ClaimMixLabRunUseCase>();
            services.AddScoped<ICompleteMixLabRunUseCase, CompleteMixLabRunUseCase>();
            services.AddScoped<IFailMixLabRunUseCase, FailMixLabRunUseCase>();
            services.AddScoped<IMixLabRunQueryUseCase, MixLabRunQueryUseCase>();
            services.AddScoped<IOpenMixLabRunArtifactUseCase, OpenMixLabRunArtifactUseCase>();

            // History and feedback use cases (A4).
            services.AddScoped<IGetMixLabHistoryUseCase, GetMixLabHistoryUseCase>();
            services.AddScoped<IPutMixLabHistoryUseCase, PutMixLabHistoryUseCase>();
            services.AddScoped<ISubmitMixLabConceptFeedbackUseCase, SubmitMixLabConceptFeedbackUseCase>();
            services.AddScoped<IGetPendingMixLabFeedbackUseCase, GetPendingMixLabFeedbackUseCase>();
            services.AddScoped<IAckMixLabFeedbackUseCase, AckMixLabFeedbackUseCase>();

            return services;
        }
    }
}
