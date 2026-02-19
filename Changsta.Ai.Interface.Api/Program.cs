using System.Text.Json;
using Changsta.Ai.Core.BusinessProcesses.Recommendations;
using Changsta.Ai.Core.Contracts.Ai;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Contracts.Recommendations;
using Changsta.Ai.Infrastructure.Services.Ai.Configuration;
using Changsta.Ai.Infrastructure.Services.Ai.Recommenders;
using Changsta.Ai.Infrastructure.Services.SoundCloud.Catalogue;
using Changsta.Ai.Interface.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpClient();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddHttpClient();

builder.Services.Configure<OpenAiOptions>(
    builder.Configuration.GetSection("OpenAI"));

builder.Services.AddScoped<IMixCatalogueProvider>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var configuration = sp.GetRequiredService<IConfiguration>();

    string rssUrl = configuration["SoundCloud:RssUrl"]
        ?? throw new InvalidOperationException("SoundCloud:RssUrl is not configured.");

    return new SoundCloudRssMixCatalogueProvider(
        httpClientFactory.CreateClient(),
        rssUrl);
});

builder.Services.AddScoped<IMixRecommendationUseCase, MixRecommendationUseCase>();
builder.Services.AddScoped<IMixAiRecommender, SemanticKernelMixAiRecommender>();

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
