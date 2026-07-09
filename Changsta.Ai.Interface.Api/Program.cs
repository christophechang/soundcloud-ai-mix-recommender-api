using System.Text.Json;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Changsta.Ai.Core.BusinessProcesses.Catalogue;
using Changsta.Ai.Core.BusinessProcesses.Diagnostics;
using Changsta.Ai.Core.BusinessProcesses.Radio;
using Changsta.Ai.Core.BusinessProcesses.Recommendations;
using Changsta.Ai.Core.Contracts.Ai;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Contracts.Diagnostics;
using Changsta.Ai.Core.Contracts.Radio;
using Changsta.Ai.Core.Contracts.Recommendations;
using Changsta.Ai.Infrastructure.Services.Ai.Configuration;
using Changsta.Ai.Infrastructure.Services.Ai.Recommenders;
using Changsta.Ai.Infrastructure.Services.Azure;
using Changsta.Ai.Infrastructure.Services.Azure.Catalogue;
using Changsta.Ai.Infrastructure.Services.Azure.Diagnostics;
using Changsta.Ai.Infrastructure.Services.SoundCloud.Catalogue;
using Changsta.Ai.Interface.Api.Cors;
using Changsta.Ai.Interface.Api.Middleware;
using Changsta.Ai.Interface.Api.MixLab;
using Changsta.Ai.Interface.Api.RateLimiting;
using Changsta.Ai.Interface.Api.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    Path.Combine(builder.Environment.ContentRootPath, "config", "mood_weights.json"),
    optional: true,
    reloadOnChange: false);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

// Origins come from configuration (Cors:AllowedOrigins); localhost is appended only in
// Development and production is validated to reject non-https/localhost origins. See issue #32.
string[] allowedOrigins = CorsOriginResolver.Resolve(builder.Configuration, builder.Environment);

builder.Services.AddCors(options =>
{
    options.AddPolicy("ChangstaSite", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .WithMethods("GET", "POST", "PUT", "OPTIONS")
            .WithHeaders("Content-Type", "Authorization", "Content-Encoding")
            .WithExposedHeaders("ETag")
            .SetPreflightMaxAge(TimeSpan.FromHours(12));
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

string? aiConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];

if (!string.IsNullOrEmpty(aiConnectionString))
{
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor(o =>
        {
            o.ConnectionString = aiConnectionString;
        })
        .WithMetrics(m => m.AddMeter(CatalogueMetrics.MeterName));
}

builder.Services.AddHealthChecks();

builder.Services.AddHttpClient();
builder.Services.AddHttpClient("SoundCloudRss", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddMemoryCache();

bool trustCloudflareHeader = builder.Configuration.GetValue<bool>("RateLimiting:TrustCloudflareHeader");

// Global per-IP default policy on every endpoint, with stricter named policies for the AI
// recommend endpoint and the privileged mutation/diagnostics endpoints. See issue #35.
builder.Services.AddRateLimiter(options => RateLimitPolicies.Configure(options, trustCloudflareHeader));

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton<IValidateOptions<OpenAiOptions>, OpenAiOptionsValidator>();
builder.Services.AddOptions<OpenAiOptions>()
    .Bind(builder.Configuration.GetSection("OpenAI"))
    .ValidateOnStart();

builder.Services.AddAzureBlobMixCatalog(builder.Configuration);
builder.Services.AddAzureDiagnostics(builder.Configuration);
builder.Services.AddMixLabServices(builder.Configuration);

var rawMoodWeights = builder.Configuration
    .GetSection("MoodWeights")
    .Get<Dictionary<string, double>>() ?? new Dictionary<string, double>();
IReadOnlyDictionary<string, double> moodWeights =
    new Dictionary<string, double>(rawMoodWeights, StringComparer.OrdinalIgnoreCase);
builder.Services.AddSingleton(moodWeights);

builder.Services.AddSingleton<ICatalogCacheInvalidator, CatalogCacheInvalidator>();

// Fail fast at startup if the SoundCloud feed isn't configured. appsettings.json ships an empty
// default (each operator supplies their own numeric user id via env vars / user-secrets — see
// README), and warmup swallows exceptions, so without this guard a missing value would only
// surface as a confusing runtime error on the first request.
if (string.IsNullOrWhiteSpace(builder.Configuration["SoundCloud:RssUrl"]))
{
    throw new InvalidOperationException(
        "SoundCloud:RssUrl is not configured. Set it via environment variables or user-secrets (see README).");
}

// Fail closed: the privileged endpoints (catalog flush/delete, diagnostics) are guarded by the
// Catalog:FlushSecret bearer secret. Outside Development, refuse to start when it is unset so the
// endpoints can never run unauthenticated. See issues #31 / #85.
if (!builder.Environment.IsDevelopment()
    && string.IsNullOrWhiteSpace(builder.Configuration["Catalog:FlushSecret"]))
{
    throw new InvalidOperationException(
        "Catalog:FlushSecret must be configured in non-Development environments; it protects the flush, delete, and diagnostics endpoints.");
}

// Fail closed: the MixLab Anywhere endpoints (uploads, run queue/worker) are guarded by the
// MixLab:ApiSecret bearer secret (see BearerSecretAttribute's docstring for the startup-refusal
// pattern this mirrors). Outside Development, refuse to start when it is unset so those endpoints
// can never run unauthenticated. See issue #132.
if (!builder.Environment.IsDevelopment()
    && string.IsNullOrWhiteSpace(builder.Configuration["MixLab:ApiSecret"]))
{
    throw new InvalidOperationException(
        "MixLab:ApiSecret must be configured in non-Development environments; it protects the MixLab upload and run-queue endpoints.");
}

// SoundCloudRssMixCatalogueProvider is registered as its concrete type (not as IMixCatalogueProvider)
// to avoid a circular DI graph: BlobBackedMixCatalogueProvider also implements IMixCatalogueProvider
// and wraps SoundCloudRssMixCatalogueProvider as its inner provider.
// Singleton: the catalogue providers are stateless apart from the shared cache + the rebuild
// semaphore, so a single instance per process is correct (and lets the semaphore be an instance
// field rather than static — see issue #56). All dependencies below are singletons too.
builder.Services.AddSingleton<SoundCloudRssMixCatalogueProvider>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var configuration = sp.GetRequiredService<IConfiguration>();
    var cache = sp.GetRequiredService<IMemoryCache>();
    var invalidator = sp.GetRequiredService<ICatalogCacheInvalidator>();
    var logger = sp.GetRequiredService<ILogger<SoundCloudRssMixCatalogueProvider>>();

    string? rssUrl = configuration["SoundCloud:RssUrl"];
    if (string.IsNullOrWhiteSpace(rssUrl))
    {
        throw new InvalidOperationException("SoundCloud:RssUrl is not configured.");
    }

    return new SoundCloudRssMixCatalogueProvider(
        httpClientFactory.CreateClient("SoundCloudRss"),
        rssUrl,
        cache,
        invalidator,
        logger);
});

builder.Services.AddSingleton<IMixCatalogueProvider>(sp =>
{
    var inner = sp.GetRequiredService<SoundCloudRssMixCatalogueProvider>();
    var repo = sp.GetRequiredService<IBlobMixCatalogueRepository>();
    var cache = sp.GetRequiredService<IMemoryCache>();
    var invalidator = sp.GetRequiredService<ICatalogCacheInvalidator>();
    var logger = sp.GetRequiredService<ILogger<BlobBackedMixCatalogueProvider>>();
    var weights = sp.GetRequiredService<IReadOnlyDictionary<string, double>>();
    var enrichmentRepo = sp.GetRequiredService<IMoodWeightEnrichmentRepository>();
    var enricher = sp.GetRequiredService<IMoodWeightEnricher>();

    return new BlobBackedMixCatalogueProvider(inner, repo, cache, invalidator, logger, weights, enrichmentRepo, enricher);
});

builder.Services.AddScoped<ICatalogFlushUseCase, CatalogFlushUseCase>();
builder.Services.AddScoped<IDeleteMixUseCase>(sp =>
{
    var deleter = sp.GetRequiredService<ICatalogMixDeleter>();
    var invalidator = sp.GetRequiredService<ICatalogCacheInvalidator>();
    var provider = sp.GetRequiredService<IMixCatalogueProvider>();
    return new DeleteMixUseCase(deleter, invalidator, provider);
});
builder.Services.AddScoped<IMixRecommendationUseCase, MixRecommendationUseCase>();
builder.Services.AddRadioScheduling();
builder.Services.AddScoped<IGetErrorInsightsUseCase, GetErrorInsightsUseCase>();
builder.Services.AddScoped<IMixAiRecommender, OpenAiMixRecommender>();

// Singleton so it can be consumed by the singleton catalogue provider without a captive
// dependency; AiMoodWeightEnricher holds only a ChatClient and has no per-request state (#56).
builder.Services.AddSingleton<IMoodWeightEnricher, AiMoodWeightEnricher>();
builder.Services.AddHostedService<CatalogWarmupService>();

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Apply CORS here, before auth and before endpoints
app.UseCors("ChangstaSite");

app.UseRateLimiter();

app.UseAuthorization();

app.MapControllers();

app.MapGet("/", () => Results.NoContent());

// Health probes must not be throttled by the global limiter or the platform may mark the
// instance unhealthy under load.
app.MapHealthChecks("/health").DisableRateLimiting();

app.Run();
