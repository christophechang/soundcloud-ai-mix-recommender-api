using System.Text.Json;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Changsta.Ai.Core.BusinessProcesses.Recommendations;
using Changsta.Ai.Core.Contracts.Ai;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Contracts.Recommendations;
using Changsta.Ai.Infrastructure.Services.Ai.Configuration;
using Changsta.Ai.Infrastructure.Services.Ai.Recommenders;
using Changsta.Ai.Infrastructure.Services.Azure;
using Changsta.Ai.Infrastructure.Services.Azure.Catalogue;
using Changsta.Ai.Infrastructure.Services.SoundCloud.Catalogue;
using Changsta.Ai.Interface.Api.Middleware;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

var allowedOrigins = new[]
{
    "https://changsta.com",
    "https://www.changsta.com",
    "http://localhost:8080",
};

builder.Services.AddCors(options =>
{
    options.AddPolicy("ChangstaSite", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .WithMethods("GET", "POST", "OPTIONS")
            .WithHeaders("Content-Type")
            .SetPreflightMaxAge(TimeSpan.FromHours(12));
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

string? aiConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];

if (!string.IsNullOrEmpty(aiConnectionString))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(o =>
    {
        o.ConnectionString = aiConnectionString;
    });
}

builder.Services.AddHealthChecks();

builder.Services.AddHttpClient();
builder.Services.AddHttpClient("SoundCloudRss", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddMemoryCache();

bool trustCloudflareHeader = builder.Configuration.GetValue<bool>("RateLimiting:TrustCloudflareHeader");

builder.Services.AddRateLimiter(options =>
{
    // 10 requests per minute per client IP.
    // CF-Connecting-IP is only trusted when RateLimiting:TrustCloudflareHeader is true (prod only).
    // In all other environments the TCP RemoteIpAddress is used directly.
    options.AddPolicy("recommend", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: (trustCloudflareHeader
                ? httpContext.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
                : null)
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        context.HttpContext.Response.Headers["Retry-After"] = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Too many requests. Please wait a moment and try again." },
            cancellationToken).ConfigureAwait(false);
    };
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.Configure<OpenAiOptions>(
    builder.Configuration.GetSection("OpenAI"));

builder.Services.AddAzureBlobMixCatalog(builder.Configuration);

// SoundCloudRssMixCatalogueProvider is registered as its concrete type (not as IMixCatalogueProvider)
// to avoid a circular DI graph: BlobBackedMixCatalogueProvider also implements IMixCatalogueProvider
// and wraps SoundCloudRssMixCatalogueProvider as its inner provider.
builder.Services.AddScoped<SoundCloudRssMixCatalogueProvider>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var configuration = sp.GetRequiredService<IConfiguration>();
    var cache = sp.GetRequiredService<IMemoryCache>();
    var logger = sp.GetRequiredService<ILogger<SoundCloudRssMixCatalogueProvider>>();

    string rssUrl = configuration["SoundCloud:RssUrl"]
        ?? throw new InvalidOperationException("SoundCloud:RssUrl is not configured.");

    return new SoundCloudRssMixCatalogueProvider(
        httpClientFactory.CreateClient("SoundCloudRss"),
        rssUrl,
        cache,
        logger);
});

builder.Services.AddScoped<IMixCatalogueProvider>(sp =>
{
    var inner = sp.GetRequiredService<SoundCloudRssMixCatalogueProvider>();
    var repo = sp.GetRequiredService<IBlobMixCatalogueRepository>();
    var cache = sp.GetRequiredService<IMemoryCache>();
    var logger = sp.GetRequiredService<ILogger<BlobBackedMixCatalogueProvider>>();

    return new BlobBackedMixCatalogueProvider(inner, repo, cache, logger);
});

builder.Services.AddScoped<IMixRecommendationUseCase, MixRecommendationUseCase>();
builder.Services.AddScoped<IMixAiRecommender, OpenAiMixRecommender>();

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

app.MapHealthChecks("/health");

app.Run();
