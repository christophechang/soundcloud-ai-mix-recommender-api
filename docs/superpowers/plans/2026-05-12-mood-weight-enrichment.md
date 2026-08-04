# Mood Weight Enrichment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** After RSS parsing and catalog merge, detect moods not in `mood_weights.json`, score them via a single AI call with all existing weights as context, persist additions to a blob sidecar, and merge into warmth scoring — skipping the AI call entirely when no new moods are found.

**Architecture:** A new `IMoodWeightEnricher` contract (in `Core.Contracts/Ai`) is implemented by `AiMoodWeightEnricher` (AI project). `BlobBackedMixCatalogueProvider` gets two new optional nullable deps: `IMoodWeightEnrichmentRepository` (blob sidecar reader/writer, Azure project) and `IMoodWeightEnricher`. On each cache miss, it diffs all catalog moods against base + sidecar weights, calls AI only when unknowns are found, writes the sidecar, then runs warmth scoring with the merged effective weights.

**Tech Stack:** C# 10 / .NET 10, NUnit + FluentAssertions, `OpenAI.Chat.ChatClient`, Azure Blob Storage SDK, `System.Text.Json`

---

## File Map

| File | Status | Responsibility |
|------|--------|---------------|
| `Changsta.Ai.Core.Contracts/Ai/IMoodWeightEnricher.cs` | **Create** | Public contract between Azure provider and AI implementation |
| `Changsta.Ai.Infrastructure.Services.Azure/Catalogue/IMoodWeightEnrichmentRepository.cs` | **Create** | Public interface for reading/writing sidecar blob |
| `Changsta.Ai.Infrastructure.Services.Azure/Catalogue/BlobMoodWeightEnrichmentRepository.cs` | **Create** | Reads/writes `mood_weights_enriched.json` from blob |
| `Changsta.Ai.Infrastructure.Services.Azure/Configuration/BlobCatalogOptions.cs` | **Modify** | Add `EnrichedMoodWeightsBlobName` property with default |
| `Changsta.Ai.Infrastructure.Services.Azure/ServiceCollectionExtensions.cs` | **Modify** | Register `IMoodWeightEnrichmentRepository` |
| `Changsta.Ai.Infrastructure.Services.Ai/Recommenders/AiMoodWeightEnricher.cs` | **Create** | AI call, prompt building, response parsing (internal static helpers) |
| `Changsta.Ai.Infrastructure.Services.Azure/Catalogue/BlobBackedMixCatalogueProvider.cs` | **Modify** | Add enrichment deps, load sidecar, diff moods, call enricher, use merged weights |
| `Changsta.Ai.Interface.Api/Program.cs` | **Modify** | Register `IMoodWeightEnricher`, update provider factory |
| `Changsta.Ai.Tests.Unit/Recommenders/AiMoodWeightEnricherTests.cs` | **Create** | Tests for `BuildPrompt` and `ParseResponse` internal static methods |
| `Changsta.Ai.Tests.Unit/Catalogue/BlobBackedMixCatalogueProviderTests.cs` | **Modify** | Add enrichment behaviour tests |

---

## Task 1: `IMoodWeightEnricher` contract

**Files:**
- Create: `Changsta.Ai.Core.Contracts/Ai/IMoodWeightEnricher.cs`

- [ ] **Step 1: Create the interface**

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.Ai
{
    public interface IMoodWeightEnricher
    {
        Task<IReadOnlyDictionary<string, double>> EnrichAsync(
            IReadOnlyDictionary<string, double> existingWeights,
            IReadOnlyList<string> newMoods,
            CancellationToken cancellationToken = default);
    }
}
```

- [ ] **Step 2: Build to verify no errors**

```bash
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```
Expected: build succeeds, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Changsta.Ai.Core.Contracts/Ai/IMoodWeightEnricher.cs
git commit -m "feat: add IMoodWeightEnricher contract"
```

---

## Task 2: `IMoodWeightEnrichmentRepository` and `BlobCatalogOptions` extension

**Files:**
- Create: `Changsta.Ai.Infrastructure.Services.Azure/Catalogue/IMoodWeightEnrichmentRepository.cs`
- Modify: `Changsta.Ai.Infrastructure.Services.Azure/Configuration/BlobCatalogOptions.cs`

- [ ] **Step 1: Create the repository interface**

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    public interface IMoodWeightEnrichmentRepository
    {
        Task<IReadOnlyDictionary<string, double>> ReadAsync(CancellationToken cancellationToken);

        Task WriteAsync(IReadOnlyDictionary<string, double> weights, CancellationToken cancellationToken);
    }
}
```

- [ ] **Step 2: Add `EnrichedMoodWeightsBlobName` to `BlobCatalogOptions`**

Current file (`Changsta.Ai.Infrastructure.Services.Azure/Configuration/BlobCatalogOptions.cs`):
```csharp
namespace Changsta.Ai.Infrastructure.Services.Azure.Configuration
{
    public sealed class BlobCatalogOptions
    {
        /// <summary>Used for local development (Azurite or real connection string). Leave empty in production.</summary>
        public string? ConnectionString { get; init; }

        /// <summary>Blob service endpoint URL (e.g. https://account.blob.core.windows.net). Used in production with Managed Identity.</summary>
        public string? ServiceEndpoint { get; init; }

        required public string ContainerName { get; init; }

        required public string BlobName { get; init; }
    }
}
```

Add one property before the closing brace:
```csharp
        public string EnrichedMoodWeightsBlobName { get; init; } = "mood_weights_enriched.json";
```

- [ ] **Step 3: Build**

```bash
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Changsta.Ai.Infrastructure.Services.Azure/Catalogue/IMoodWeightEnrichmentRepository.cs
git add Changsta.Ai.Infrastructure.Services.Azure/Configuration/BlobCatalogOptions.cs
git commit -m "feat: add IMoodWeightEnrichmentRepository and enriched blob name config"
```

---

## Task 3: `BlobMoodWeightEnrichmentRepository` implementation

**Files:**
- Create: `Changsta.Ai.Infrastructure.Services.Azure/Catalogue/BlobMoodWeightEnrichmentRepository.cs`
- Modify: `Changsta.Ai.Infrastructure.Services.Azure/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Create the blob repository**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Changsta.Ai.Infrastructure.Services.Azure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    internal sealed class BlobMoodWeightEnrichmentRepository : IMoodWeightEnrichmentRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        private readonly BlobContainerClient _containerClient;
        private readonly string _blobName;
        private readonly ILogger<BlobMoodWeightEnrichmentRepository> _logger;

        public BlobMoodWeightEnrichmentRepository(
            IOptions<BlobCatalogOptions> options,
            ILogger<BlobMoodWeightEnrichmentRepository> logger)
        {
            var resolved = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            bool hasConnectionString = !string.IsNullOrWhiteSpace(resolved.ConnectionString);
            bool hasServiceEndpoint = !string.IsNullOrWhiteSpace(resolved.ServiceEndpoint);

            if (!hasConnectionString && !hasServiceEndpoint)
            {
                throw new InvalidOperationException(
                    "Either Azure:BlobCatalog:ConnectionString or Azure:BlobCatalog:ServiceEndpoint must be configured.");
            }

            if (string.IsNullOrWhiteSpace(resolved.ContainerName))
            {
                throw new InvalidOperationException("Azure:BlobCatalog:ContainerName is not configured.");
            }

            if (hasServiceEndpoint)
            {
                var containerUri = new Uri(
                    resolved.ServiceEndpoint!.TrimEnd('/') + "/" + resolved.ContainerName);
                _containerClient = new BlobContainerClient(containerUri, new DefaultAzureCredential());
            }
            else
            {
                _containerClient = new BlobContainerClient(resolved.ConnectionString, resolved.ContainerName);
            }

            _blobName = resolved.EnrichedMoodWeightsBlobName;
        }

        public async Task<IReadOnlyDictionary<string, double>> ReadAsync(CancellationToken cancellationToken)
        {
            try
            {
                var blobClient = _containerClient.GetBlobClient(_blobName);
                var download = await blobClient
                    .DownloadContentAsync(cancellationToken)
                    .ConfigureAwait(false);

                var dict = JsonSerializer.Deserialize<Dictionary<string, double>>(
                    download.Value.Content.ToStream(), JsonOptions)
                    ?? new Dictionary<string, double>();

                return new Dictionary<string, double>(dict, StringComparer.OrdinalIgnoreCase);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogInformation("Enriched mood weights blob not found — starting with empty enrichment.");
                return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public async Task WriteAsync(IReadOnlyDictionary<string, double> weights, CancellationToken cancellationToken)
        {
            try
            {
                await _containerClient
                    .CreateIfNotExistsAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                using var stream = new MemoryStream();
                await JsonSerializer.SerializeAsync(stream, weights, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                stream.Position = 0;

                var blobClient = _containerClient.GetBlobClient(_blobName);
                await blobClient
                    .UploadAsync(stream, overwrite: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write enriched mood weights blob.");
            }
        }
    }
}
```

- [ ] **Step 2: Register in `ServiceCollectionExtensions.AddAzureBlobMixCatalog`**

Add after the `IBlobMixCatalogueRepository` line:
```csharp
services.AddScoped<IMoodWeightEnrichmentRepository, BlobMoodWeightEnrichmentRepository>();
```

Full updated method:
```csharp
public static IServiceCollection AddAzureBlobMixCatalog(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.Configure<BlobCatalogOptions>(configuration.GetSection("Azure:BlobCatalog"));
    services.AddScoped<IBlobMixCatalogueRepository, BlobMixCatalogueRepository>();
    services.AddScoped<IMoodWeightEnrichmentRepository, BlobMoodWeightEnrichmentRepository>();
    services.AddScoped<Changsta.Ai.Core.Contracts.Catalogue.ICatalogMixDeleter, BlobCatalogMixDeleter>();

    return services;
}
```

- [ ] **Step 3: Build**

```bash
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Changsta.Ai.Infrastructure.Services.Azure/Catalogue/BlobMoodWeightEnrichmentRepository.cs
git add Changsta.Ai.Infrastructure.Services.Azure/ServiceCollectionExtensions.cs
git commit -m "feat: add BlobMoodWeightEnrichmentRepository for AI-scored mood sidecar"
```

---

## Task 4: Write failing tests for `AiMoodWeightEnricher`

**Files:**
- Create: `Changsta.Ai.Tests.Unit/Recommenders/AiMoodWeightEnricherTests.cs`

Note: `InternalsVisibleTo("Changsta.Ai.Tests.Unit")` is set on the AI project. `BuildPrompt` and `ParseResponse` will be `internal static` methods — accessible from this test file.

- [ ] **Step 1: Create the test file**

```csharp
using System.Collections.Generic;
using Changsta.Ai.Infrastructure.Services.Ai.Recommenders;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Recommenders
{
    [TestFixture]
    public sealed class AiMoodWeightEnricherTests
    {
        private static readonly IReadOnlyDictionary<string, double> SampleWeights =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["warm"] = 2.0,
                ["dark"] = -2.0,
                ["energetic"] = 1.0,
                ["neutral"] = 0.0,
            };

        [Test]
        public void BuildPrompt_includes_scale_description()
        {
            var prompt = AiMoodWeightEnricher.BuildPrompt(SampleWeights, new[] { "melancholic" });

            prompt.Should().Contain("-2.0");
            prompt.Should().Contain("2.0");
        }

        [Test]
        public void BuildPrompt_includes_all_existing_weights()
        {
            var prompt = AiMoodWeightEnricher.BuildPrompt(SampleWeights, new[] { "melancholic" });

            prompt.Should().Contain("warm");
            prompt.Should().Contain("dark");
            prompt.Should().Contain("energetic");
            prompt.Should().Contain("neutral");
        }

        [Test]
        public void BuildPrompt_includes_new_moods()
        {
            var prompt = AiMoodWeightEnricher.BuildPrompt(SampleWeights, new[] { "melancholic", "bittersweet" });

            prompt.Should().Contain("melancholic");
            prompt.Should().Contain("bittersweet");
        }

        [Test]
        public void ParseResponse_extracts_valid_mood_weight_pairs()
        {
            var json = """{ "melancholic": -0.5, "bittersweet": 0.3 }""";
            var requested = new[] { "melancholic", "bittersweet" };

            var result = AiMoodWeightEnricher.ParseResponse(json, requested);

            result.Should().ContainKey("melancholic").WhoseValue.Should().BeApproximately(-0.5, 0.001);
            result.Should().ContainKey("bittersweet").WhoseValue.Should().BeApproximately(0.3, 0.001);
        }

        [Test]
        public void ParseResponse_clamps_values_above_two()
        {
            var json = """{ "intense": 5.0 }""";
            var requested = new[] { "intense" };

            var result = AiMoodWeightEnricher.ParseResponse(json, requested);

            result["intense"].Should().Be(2.0);
        }

        [Test]
        public void ParseResponse_clamps_values_below_minus_two()
        {
            var json = """{ "brutal": -9.0 }""";
            var requested = new[] { "brutal" };

            var result = AiMoodWeightEnricher.ParseResponse(json, requested);

            result["brutal"].Should().Be(-2.0);
        }

        [Test]
        public void ParseResponse_ignores_moods_not_in_requested_list()
        {
            var json = """{ "melancholic": -0.5, "surprise_extra": 1.0 }""";
            var requested = new[] { "melancholic" };

            var result = AiMoodWeightEnricher.ParseResponse(json, requested);

            result.Should().ContainKey("melancholic");
            result.Should().NotContainKey("surprise_extra");
        }

        [Test]
        public void ParseResponse_returns_empty_on_malformed_json()
        {
            var result = AiMoodWeightEnricher.ParseResponse("not json at all", new[] { "melancholic" });

            result.Should().BeEmpty();
        }

        [Test]
        public void ParseResponse_returns_empty_on_empty_json_object()
        {
            var result = AiMoodWeightEnricher.ParseResponse("{}", new[] { "melancholic" });

            result.Should().BeEmpty();
        }

        [Test]
        public void ParseResponse_is_case_insensitive_for_mood_names()
        {
            var json = """{ "Melancholic": -0.5 }""";
            var requested = new[] { "melancholic" };

            var result = AiMoodWeightEnricher.ParseResponse(json, requested);

            result.Should().ContainKey("Melancholic");
        }
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure because `AiMoodWeightEnricher` doesn't exist yet**

```bash
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```
Expected: compile error referencing `AiMoodWeightEnricher`.

---

## Task 5: Implement `AiMoodWeightEnricher`

**Files:**
- Create: `Changsta.Ai.Infrastructure.Services.Ai/Recommenders/AiMoodWeightEnricher.cs`

- [ ] **Step 1: Create the implementation**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Ai;
using Changsta.Ai.Infrastructure.Services.Ai.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace Changsta.Ai.Infrastructure.Services.Ai.Recommenders
{
    internal sealed class AiMoodWeightEnricher : IMoodWeightEnricher
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly ChatClient _chat;
        private readonly ILogger<AiMoodWeightEnricher> _logger;

        public AiMoodWeightEnricher(IOptions<OpenAiOptions> options, ILogger<AiMoodWeightEnricher> logger)
        {
            var resolved = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(resolved.ApiKey))
            {
                throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
            }

            if (string.IsNullOrWhiteSpace(resolved.Model))
            {
                throw new InvalidOperationException("OpenAI:Model is not configured.");
            }

            _chat = new ChatClient(model: resolved.Model, apiKey: resolved.ApiKey);
        }

        public async Task<IReadOnlyDictionary<string, double>> EnrichAsync(
            IReadOnlyDictionary<string, double> existingWeights,
            IReadOnlyList<string> newMoods,
            CancellationToken cancellationToken = default)
        {
            if (newMoods.Count == 0)
            {
                return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }

            string prompt = BuildPrompt(existingWeights, newMoods);

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are calibrating a mood weight scale. Output must be strict JSON only. Do not wrap output in ``` fences."),
                new UserChatMessage(prompt),
            };

            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                ChatCompletion completion = await _chat
                    .CompleteChatAsync(messages, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                string rawContent = completion.Content.Count > 0
                    ? completion.Content[0].Text ?? string.Empty
                    : string.Empty;

                stopwatch.Stop();
                _logger.LogInformation(
                    "Mood weight enrichment AI call completed. newMoodCount={NewMoodCount} openAiMs={OpenAiMs}",
                    newMoods.Count,
                    stopwatch.ElapsedMilliseconds);

                IReadOnlyDictionary<string, double> result = ParseResponse(rawContent, newMoods);

                _logger.LogInformation(
                    "Mood weight enrichment parsed. requested={Requested} scored={Scored} moods={Moods}",
                    newMoods.Count,
                    result.Count,
                    string.Join(", ", result.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)));

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI mood weight enrichment call failed.");
                return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }
        }

        internal static string BuildPrompt(
            IReadOnlyDictionary<string, double> existingWeights,
            IReadOnlyList<string> newMoods)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are calibrating a mood weight scale where -2.0 = cold/aggressive and +2.0 = warm/euphoric.");
            sb.AppendLine();
            sb.AppendLine("Existing mood weights for context (sorted by value):");

            foreach (var kvp in existingWeights
                .OrderBy(k => k.Value)
                .ThenBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine(FormattableString.Invariant($"  {kvp.Key}: {kvp.Value:F1}"));
            }

            sb.AppendLine();
            sb.AppendLine("Score the following new moods on the same scale, keeping scores consistent with the existing entries:");
            sb.AppendLine(JsonSerializer.Serialize(newMoods));
            sb.AppendLine();
            sb.Append("Respond with strict JSON only, no explanation: { \"mood\": value, ... }");
            return sb.ToString();
        }

        internal static IReadOnlyDictionary<string, double> ParseResponse(
            string json,
            IReadOnlyList<string> requestedMoods)
        {
            var requested = new HashSet<string>(requestedMoods, StringComparer.OrdinalIgnoreCase);

            try
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, double>>(json, JsonOptions)
                    ?? new Dictionary<string, double>();

                var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                foreach (var kvp in raw)
                {
                    if (requested.Contains(kvp.Key))
                    {
                        result[kvp.Key] = Math.Clamp(kvp.Value, -2.0, 2.0);
                    }
                }

                return result;
            }
            catch (JsonException)
            {
                return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests from Task 4**

```bash
dotnet test soundcloud-ai-mix-recommender-api.sln --no-build --filter "FullyQualifiedName~AiMoodWeightEnricherTests"
```
Expected: all 9 tests pass.

- [ ] **Step 3: Commit**

```bash
git add Changsta.Ai.Infrastructure.Services.Ai/Recommenders/AiMoodWeightEnricher.cs
git add Changsta.Ai.Tests.Unit/Recommenders/AiMoodWeightEnricherTests.cs
git commit -m "feat: add AiMoodWeightEnricher with prompt building and response parsing"
```

---

## Task 6: Modify `BlobBackedMixCatalogueProvider` — write failing tests first

**Files:**
- Modify: `Changsta.Ai.Tests.Unit/Catalogue/BlobBackedMixCatalogueProviderTests.cs`

These tests verify the enrichment behaviour at the provider level using stub implementations of `IMoodWeightEnricher` and `IMoodWeightEnrichmentRepository`.

- [ ] **Step 1: Add stub classes and test cases to the existing test file**

Add to the `BlobBackedMixCatalogueProviderTests` class, inside the `[TestFixture]`:

**First, add a `moods` parameter to `MakeMix`** (find the `MakeMix` helper at the bottom of the class and add a `moods` parameter):

```csharp
private static Mix MakeMix(
    string id,
    string url,
    string title = "Test Mix",
    string? description = null,
    string? duration = null,
    string? imageUrl = null,
    string genre = "dnb",
    DateTimeOffset? publishedAt = null,
    IReadOnlyList<Track>? tracklist = null,
    IReadOnlyList<string>? moods = null)
{
    return new Mix
    {
        Id = id,
        Title = title,
        Url = url,
        Genre = genre,
        Energy = "peak",
        Description = description,
        Duration = duration,
        ImageUrl = imageUrl,
        PublishedAt = publishedAt,
        Tracklist = tracklist ?? Array.Empty<Track>(),
        Moods = moods ?? Array.Empty<string>(),
    };
}
```

**Add stub implementations after `StubBlobRepository`:**

```csharp
private sealed class StubMoodWeightEnricher : IMoodWeightEnricher
{
    public IReadOnlyList<string>? ReceivedNewMoods { get; private set; }
    public IReadOnlyDictionary<string, double>? ReceivedExistingWeights { get; private set; }
    public int CallCount { get; private set; }
    public IReadOnlyDictionary<string, double> ReturnValue { get; set; } =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyDictionary<string, double>> EnrichAsync(
        IReadOnlyDictionary<string, double> existingWeights,
        IReadOnlyList<string> newMoods,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        ReceivedExistingWeights = existingWeights;
        ReceivedNewMoods = newMoods;
        return Task.FromResult(ReturnValue);
    }
}

private sealed class StubMoodWeightEnrichmentRepository : IMoodWeightEnrichmentRepository
{
    public IReadOnlyDictionary<string, double> StoredWeights { get; set; } =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, double>? WrittenWeights { get; private set; }
    public int WriteCallCount { get; private set; }

    public Task<IReadOnlyDictionary<string, double>> ReadAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(StoredWeights);
    }

    public Task WriteAsync(IReadOnlyDictionary<string, double> weights, CancellationToken cancellationToken)
    {
        WriteCallCount++;
        WrittenWeights = weights;
        return Task.CompletedTask;
    }
}
```

**Update `BuildSut` to accept the new optional parameters:**

```csharp
private static BlobBackedMixCatalogueProvider BuildSut(
    StubBlobRepository? blobRepo = null,
    IReadOnlyList<Mix>? blobMixes = null,
    IReadOnlyList<Mix>? rssMixes = null,
    Exception? rssException = null,
    ILogger<BlobBackedMixCatalogueProvider>? logger = null,
    IReadOnlyDictionary<string, double>? moodWeights = null,
    IMoodWeightEnrichmentRepository? enrichmentRepo = null,
    IMoodWeightEnricher? enricher = null)
{
    var repo = blobRepo ?? new StubBlobRepository
    {
        BlobMixes = blobMixes ?? Array.Empty<Mix>(),
    };

    if (blobMixes is not null && blobRepo is null)
    {
        repo.BlobMixes = blobMixes;
    }

    IMixCatalogueProvider rssProvider = rssException is not null
        ? new ThrowingMixCatalogueProvider(rssException)
        : new StubMixCatalogueProvider(rssMixes ?? Array.Empty<Mix>());

    return new BlobBackedMixCatalogueProvider(
        rssProvider,
        repo,
        new MemoryCache(new MemoryCacheOptions()),
        new StubCatalogCacheInvalidator(),
        logger ?? NullLogger<BlobBackedMixCatalogueProvider>.Instance,
        moodWeights ?? new Dictionary<string, double>(),
        enrichmentRepo,
        enricher);
}
```

**Add new test cases** (insert before the `BuildSut` method):

```csharp
[Test]
public async Task GetLatestAsync_calls_enricher_when_catalog_has_unknown_moods()
{
    var rssMix = MakeMix("1", "https://sc.test/mix-1", moods: new[] { "melancholic" });
    var enricher = new StubMoodWeightEnricher();
    var enrichmentRepo = new StubMoodWeightEnrichmentRepository();

    var sut = BuildSut(
        blobMixes: Array.Empty<Mix>(),
        rssMixes: new[] { rssMix },
        enrichmentRepo: enrichmentRepo,
        enricher: enricher);

    await sut.GetLatestAsync(10, CancellationToken.None);

    Assert.That(enricher.CallCount, Is.EqualTo(1));
    Assert.That(enricher.ReceivedNewMoods, Contains.Item("melancholic"));
}

[Test]
public async Task GetLatestAsync_skips_enricher_when_all_moods_are_known()
{
    var baseWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["warm"] = 2.0 };
    var rssMix = MakeMix("1", "https://sc.test/mix-1", moods: new[] { "warm" });
    var enricher = new StubMoodWeightEnricher();

    var sut = BuildSut(
        blobMixes: Array.Empty<Mix>(),
        rssMixes: new[] { rssMix },
        moodWeights: baseWeights,
        enricher: enricher);

    await sut.GetLatestAsync(10, CancellationToken.None);

    Assert.That(enricher.CallCount, Is.EqualTo(0));
}

[Test]
public async Task GetLatestAsync_skips_enricher_when_catalog_has_no_moods()
{
    var rssMix = MakeMix("1", "https://sc.test/mix-1");
    var enricher = new StubMoodWeightEnricher();

    var sut = BuildSut(
        blobMixes: Array.Empty<Mix>(),
        rssMixes: new[] { rssMix },
        enricher: enricher);

    await sut.GetLatestAsync(10, CancellationToken.None);

    Assert.That(enricher.CallCount, Is.EqualTo(0));
}

[Test]
public async Task GetLatestAsync_passes_all_effective_weights_to_enricher_as_context()
{
    var baseWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["warm"] = 2.0 };
    var storedEnriched = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["funky"] = 1.5 };
    var rssMix = MakeMix("1", "https://sc.test/mix-1", moods: new[] { "melancholic" });

    var enricher = new StubMoodWeightEnricher();
    var enrichmentRepo = new StubMoodWeightEnrichmentRepository { StoredWeights = storedEnriched };

    var sut = BuildSut(
        blobMixes: Array.Empty<Mix>(),
        rssMixes: new[] { rssMix },
        moodWeights: baseWeights,
        enrichmentRepo: enrichmentRepo,
        enricher: enricher);

    await sut.GetLatestAsync(10, CancellationToken.None);

    Assert.That(enricher.ReceivedExistingWeights, Contains.Key("warm"));
    Assert.That(enricher.ReceivedExistingWeights, Contains.Key("funky"));
}

[Test]
public async Task GetLatestAsync_writes_sidecar_after_enricher_returns_new_scores()
{
    var rssMix = MakeMix("1", "https://sc.test/mix-1", moods: new[] { "melancholic" });
    var enricher = new StubMoodWeightEnricher
    {
        ReturnValue = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            { ["melancholic"] = -0.5 },
    };
    var enrichmentRepo = new StubMoodWeightEnrichmentRepository();

    var sut = BuildSut(
        blobMixes: Array.Empty<Mix>(),
        rssMixes: new[] { rssMix },
        enrichmentRepo: enrichmentRepo,
        enricher: enricher);

    await sut.GetLatestAsync(10, CancellationToken.None);

    Assert.That(enrichmentRepo.WriteCallCount, Is.EqualTo(1));
    Assert.That(enrichmentRepo.WrittenWeights, Contains.Key("melancholic"));
}

[Test]
public async Task GetLatestAsync_skips_sidecar_write_when_enricher_returns_empty()
{
    var rssMix = MakeMix("1", "https://sc.test/mix-1", moods: new[] { "melancholic" });
    var enricher = new StubMoodWeightEnricher(); // ReturnValue defaults to empty
    var enrichmentRepo = new StubMoodWeightEnrichmentRepository();

    var sut = BuildSut(
        blobMixes: Array.Empty<Mix>(),
        rssMixes: new[] { rssMix },
        enrichmentRepo: enrichmentRepo,
        enricher: enricher);

    await sut.GetLatestAsync(10, CancellationToken.None);

    Assert.That(enrichmentRepo.WriteCallCount, Is.EqualTo(0));
}

[Test]
public async Task GetLatestAsync_merges_sidecar_weights_with_enriched_scores_before_writing()
{
    var storedEnriched = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["funky"] = 1.5 };
    var rssMix = MakeMix("1", "https://sc.test/mix-1", moods: new[] { "melancholic" });
    var enricher = new StubMoodWeightEnricher
    {
        ReturnValue = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            { ["melancholic"] = -0.5 },
    };
    var enrichmentRepo = new StubMoodWeightEnrichmentRepository { StoredWeights = storedEnriched };

    var sut = BuildSut(
        blobMixes: Array.Empty<Mix>(),
        rssMixes: new[] { rssMix },
        enrichmentRepo: enrichmentRepo,
        enricher: enricher);

    await sut.GetLatestAsync(10, CancellationToken.None);

    Assert.That(enrichmentRepo.WrittenWeights, Contains.Key("funky"));
    Assert.That(enrichmentRepo.WrittenWeights, Contains.Key("melancholic"));
}

[Test]
public async Task GetLatestAsync_unknown_moods_deduped_before_sending_to_enricher()
{
    var mix1 = MakeMix("1", "https://sc.test/mix-1", moods: new[] { "melancholic" });
    var mix2 = MakeMix("2", "https://sc.test/mix-2", moods: new[] { "Melancholic" }); // same, different case
    var enricher = new StubMoodWeightEnricher();
    var enrichmentRepo = new StubMoodWeightEnrichmentRepository();

    var sut = BuildSut(
        blobMixes: Array.Empty<Mix>(),
        rssMixes: new[] { mix1, mix2 },
        enrichmentRepo: enrichmentRepo,
        enricher: enricher);

    await sut.GetLatestAsync(10, CancellationToken.None);

    Assert.That(enricher.ReceivedNewMoods, Has.Count.EqualTo(1));
}

[Test]
public async Task GetLatestAsync_no_enricher_configured_runs_without_exception()
{
    var rssMix = MakeMix("1", "https://sc.test/mix-1", moods: new[] { "melancholic" });

    var sut = BuildSut(
        blobMixes: Array.Empty<Mix>(),
        rssMixes: new[] { rssMix });
    // enricher and enrichmentRepo are null — feature is disabled

    var result = await sut.GetLatestAsync(10, CancellationToken.None);

    Assert.That(result, Has.Count.EqualTo(1));
}
```

- [ ] **Step 2: Run tests — expect failures because `BlobBackedMixCatalogueProvider` doesn't accept new deps yet**

```bash
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```
Expected: compile error — constructor mismatch.

---

## Task 7: Modify `BlobBackedMixCatalogueProvider` to add enrichment logic

**Files:**
- Modify: `Changsta.Ai.Infrastructure.Services.Azure/Catalogue/BlobBackedMixCatalogueProvider.cs`

- [ ] **Step 1: Add new using directives and dependencies**

Add to usings (if not already present):
```csharp
using Changsta.Ai.Core.Contracts.Ai;
```

Add to constructor parameters (after `IReadOnlyDictionary<string, double> moodWeights`):
```csharp
IMoodWeightEnrichmentRepository? enrichmentRepository = null,
IMoodWeightEnricher? moodWeightEnricher = null
```

Add to field declarations:
```csharp
private readonly IMoodWeightEnrichmentRepository? _enrichmentRepository;
private readonly IMoodWeightEnricher? _moodWeightEnricher;
```

Add to constructor body:
```csharp
_enrichmentRepository = enrichmentRepository;
_moodWeightEnricher = moodWeightEnricher;
```

- [ ] **Step 2: Replace the warmth scoring block in `GetLatestAsync`**

Replace this block (lines 98–99):
```csharp
bool warmthChanged;
merged = WarmthScorer.ComputeWarmth(merged, _moodWeights, _logger, out warmthChanged);
```

With:
```csharp
IReadOnlyDictionary<string, double> enrichedWeights =
    await LoadEnrichedWeightsSafeAsync(cancellationToken).ConfigureAwait(false);
IReadOnlyDictionary<string, double> effectiveWeights = MergeWeights(_moodWeights, enrichedWeights);

IReadOnlyList<string> unknownMoods = FindUnknownMoods(merged, effectiveWeights);
if (unknownMoods.Count > 0 && _moodWeightEnricher is not null)
{
    _logger.LogInformation(
        "Enriching {Count} unknown mood(s) via AI: {Moods}",
        unknownMoods.Count,
        string.Join(", ", unknownMoods));

    IReadOnlyDictionary<string, double> newScores =
        await EnrichMoodsSafeAsync(effectiveWeights, unknownMoods, cancellationToken).ConfigureAwait(false);

    if (newScores.Count > 0)
    {
        enrichedWeights = MergeWeights(enrichedWeights, newScores);
        effectiveWeights = MergeWeights(_moodWeights, enrichedWeights);

        if (_enrichmentRepository is not null)
        {
            await _enrichmentRepository.WriteAsync(enrichedWeights, cancellationToken).ConfigureAwait(false);
        }
    }
}

bool warmthChanged;
merged = WarmthScorer.ComputeWarmth(merged, effectiveWeights, _logger, out warmthChanged);
```

- [ ] **Step 3: Add helper methods to `BlobBackedMixCatalogueProvider`**

Add after `FetchRssSafeAsync`:

```csharp
private async Task<IReadOnlyDictionary<string, double>> LoadEnrichedWeightsSafeAsync(
    CancellationToken cancellationToken)
{
    if (_enrichmentRepository is null)
    {
        return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    try
    {
        return await _enrichmentRepository.ReadAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to load enriched mood weights — proceeding with base weights only.");
        return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }
}

private async Task<IReadOnlyDictionary<string, double>> EnrichMoodsSafeAsync(
    IReadOnlyDictionary<string, double> existingWeights,
    IReadOnlyList<string> unknownMoods,
    CancellationToken cancellationToken)
{
    try
    {
        return await _moodWeightEnricher!
            .EnrichAsync(existingWeights, unknownMoods, cancellationToken)
            .ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "AI mood enrichment failed — new moods will have no weight this cycle.");
        return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }
}

private static IReadOnlyDictionary<string, double> MergeWeights(
    IReadOnlyDictionary<string, double> baseWeights,
    IReadOnlyDictionary<string, double> additions)
{
    var merged = new Dictionary<string, double>(baseWeights, StringComparer.OrdinalIgnoreCase);
    foreach (var kvp in additions)
    {
        merged[kvp.Key] = kvp.Value;
    }

    return merged;
}

private static IReadOnlyList<string> FindUnknownMoods(
    IReadOnlyList<Mix> mixes,
    IReadOnlyDictionary<string, double> weights)
{
    return mixes
        .SelectMany(m => m.Moods)
        .Where(m => !string.IsNullOrWhiteSpace(m))
        .Select(m => m.Trim().ToLowerInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Where(m => !weights.ContainsKey(m))
        .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
```

- [ ] **Step 4: Run all provider tests**

```bash
dotnet test soundcloud-ai-mix-recommender-api.sln --no-build --filter "FullyQualifiedName~BlobBackedMixCatalogueProviderTests"
```
Expected: all tests pass, including the new enrichment ones.

- [ ] **Step 5: Run the full test suite**

```bash
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental && dotnet test soundcloud-ai-mix-recommender-api.sln --no-build
```
Expected: 0 build errors, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add Changsta.Ai.Infrastructure.Services.Azure/Catalogue/BlobBackedMixCatalogueProvider.cs
git add Changsta.Ai.Tests.Unit/Catalogue/BlobBackedMixCatalogueProviderTests.cs
git commit -m "feat: enrich unknown mood weights via AI after catalog merge"
```

---

## Task 8: Wire up DI in `Program.cs`

**Files:**
- Modify: `Changsta.Ai.Interface.Api/Program.cs`

- [ ] **Step 1: Add `IMoodWeightEnricher` registration**

Add after line 172 (`builder.Services.AddScoped<IMixAiRecommender, OpenAiMixRecommender>();`):

```csharp
builder.Services.AddScoped<IMoodWeightEnricher, AiMoodWeightEnricher>();
```

Add using at the top if not already present:
```csharp
using Changsta.Ai.Core.Contracts.Ai;
using Changsta.Ai.Infrastructure.Services.Ai.Recommenders;
using Changsta.Ai.Infrastructure.Services.Azure.Catalogue;
```

- [ ] **Step 2: Update the `IMixCatalogueProvider` factory to inject enrichment deps**

Replace the factory (lines 150–160) with:

```csharp
builder.Services.AddScoped<IMixCatalogueProvider>(sp =>
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
```

- [ ] **Step 3: Build and run full test suite**

```bash
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental && dotnet test soundcloud-ai-mix-recommender-api.sln --no-build
```
Expected: 0 errors, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add Changsta.Ai.Interface.Api/Program.cs
git commit -m "feat: wire IMoodWeightEnricher and IMoodWeightEnrichmentRepository into DI"
```

---

## Self-Review Checklist

- [x] **Spec coverage:** Single AI call per flush cycle (not per mood) — handled by `FindUnknownMoods` + single `EnrichAsync` call. AI skipped when no unknowns — guarded by `unknownMoods.Count > 0`. All existing weights sent as context — `effectiveWeights` (base + sidecar) passed to `EnrichAsync`. Blob sidecar persists additions — `IMoodWeightEnrichmentRepository.WriteAsync`. No AI call if no new moods — tested explicitly.
- [x] **Placeholders:** None — all code blocks are complete and compilable.
- [x] **Type consistency:** `IMoodWeightEnricher` signature is identical in interface, implementation, and stubs. `IMoodWeightEnrichmentRepository` matches across interface, implementation, and stubs. `BlobBackedMixCatalogueProvider` constructor parameter order is consistent between Task 7 (implementation) and Task 8 (DI wiring).
