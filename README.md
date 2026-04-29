# SoundCloud Mix Recommender API

[![CI Build](https://github.com/christophechang/soundcloud-ai-mix-recommender-api/actions/workflows/ci-build.yml/badge.svg)](https://github.com/christophechang/soundcloud-ai-mix-recommender-api/actions/workflows/ci-build.yml)
[![GitHub release](https://img.shields.io/github/v/release/christophechang/soundcloud-ai-mix-recommender-api)](https://github.com/christophechang/soundcloud-ai-mix-recommender-api/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A production-ready **.NET 10 Web API** that recommends SoundCloud DJ mixes using AI, balancing creative relevance with strict server-side validation to prevent hallucination.

The guiding principle: **`reason` is creative; `why` is evidence. Both are required. Only `why` is verified against catalogue data.**

---

## Quick Start

**Prerequisites:**

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js](https://nodejs.org/) (required only if running Azurite via `npm`)
- An OpenAI API key
- A SoundCloud numeric user ID (see [RSS Feed](#rss-feed-limitations))

**Steps:**

```bash
# 1. Start local blob storage (Azurite)
npm install -g azurite
azurite --silent --location /tmp/azurite --debug /tmp/azurite-debug.log

# 2. Set secrets
dotnet user-secrets set "OpenAI:ApiKey" "your-key-here" --project Changsta.Ai.Interface.Api
dotnet user-secrets set "OpenAI:Model" "gpt-4.1-mini" --project Changsta.Ai.Interface.Api
dotnet user-secrets set "Azure:BlobCatalog:ConnectionString" "UseDevelopmentStorage=true" --project Changsta.Ai.Interface.Api
dotnet user-secrets set "SoundCloud:RssUrl" "https://feeds.soundcloud.com/users/soundcloud:users:YOUR_USER_ID/sounds.rss" --project Changsta.Ai.Interface.Api

# 3. Build and run
dotnet restore
dotnet build soundcloud-ai-mix-recommender-api.sln
dotnet run --project Changsta.Ai.Interface.Api
```

Swagger UI: `http://localhost:<port>/swagger` (port shown in terminal output). Swagger is only mounted when `ASPNETCORE_ENVIRONMENT=Development` — this is the default for `dotnet run` via the `launchSettings.json` profile.

---

## What's new in v1.12

- **Related mixes on every catalog item.** Each mix returned by `GET /api/catalog/mixes` and `GET /api/catalog/artists/{name}/mixes` now includes a `relatedMixes` array of up to 6 slim references (title, URL, artwork URL), pre-computed and ranked by relevance. Scoring uses shared exact tracks (+15 each), shared tracklist artists (+8 each), same genre (+6), same energy (+3), shared moods (+2 each), and BPM range overlap (+1). Ties are broken by most recently published. Results are computed once at cache flush time, persisted to the blob catalog, and served from the 24-hour memory cache at zero read cost.

---

## What's new in v1.11

- **Live schema sync on cache flush.** Editing the `[changsta:mix:v1 {...}]` JSON block in a SoundCloud mix description and calling `POST /api/catalog/flush` now propagates the updated genre, energy, BPM, moods, and tracklist into the blob-backed catalog. Sync only fires when the description has changed and the updated description contains a valid schema block — mixes without the schema tag are unaffected.
- **Recommendation catalog expanded to 200 mixes.** The AI recommender now receives up to 200 catalog mixes (was 50), consistent with all other catalog endpoints. The recommender's own 100-mix prompt cap still applies, so token count is unchanged.
- **Tech house genre aliases added.** `tech house` and `tech-house` now normalise to `techno` in the genre normaliser, matching the existing alias table used in recommendation filtering.
- **AI response contract tightened.** `title` and `url` fields removed from the allowed AI response schema — the server always resolves these from the catalog. Any AI response containing these fields is now rejected rather than silently ignored, eliminating wasted tokens.
- **CORS fix for catalog flush.** Browser clients can now send `Authorization: Bearer` headers to `POST /api/catalog/flush` without hitting a CORS preflight failure.

---

## What's new in v1.10

- **Canonical genre normalization.** Catalogue sync now normalizes overlapping genre aliases such as `breaks`, `uk-bass`, `ukbass`, `deep house`, `drum and bass`, and `garage` into one canonical genre value before mixes are cached, stored, indexed, or returned.
- **Cleaner genre endpoints and filters.** `GET /api/catalog/genres`, catalogue grouping, mix filtering, track summaries, and recommendation genre filtering now use the same reusable normalizer, so aliases resolve consistently across API responses.
- **Legacy catalogue cleanup on sync.** Existing blob-backed catalogue entries are normalized during sync and written back when needed, while unknown genres are preserved in lowercase form and logged once per sync run for future mapping updates.

---

## What's new in v1.9

- **Legacy RSS metadata hydration.** SoundCloud RSS items without a `[changsta:mix:v1 ...]` schema block are now still read for metadata, allowing older mixes to pick up live title, description, duration, artwork, and publish-date updates.
- **Safe cover-art backfill.** Metadata-only RSS rows can refresh matching existing blob catalogue entries without being added as new uncurated catalogue mixes.
- **Cache flush improvements.** Flushing the catalogue cache now backfills cover URLs for legacy mixes that pre-date structured JSON metadata, while preserving curated genre, energy, BPM, moods, and tracklist data.

---

## What's new in v1.8

- **Lightweight artist name endpoint.** `GET /api/catalog/artists` now returns a single unpaginated response with all artist names sorted alphabetically — `{ "artists": ["Anu", "Bicep", ...] }`. No track data, no pagination params. Optimised for autocomplete use cases.

---

## What's new in v1.7

- **RSS duration and artwork ingestion.** SoundCloud RSS processing now captures `itunes:duration` and `itunes:image` for each mix and stores them in the blob-backed catalogue cache as `duration` and `imageUrl`.
- **Blob cache metadata refresh.** Existing cached mixes are refreshed with live RSS title, description, publish date, duration, and artwork metadata while preserving curated genre, energy, BPM, moods, and tracklist data.
- **Hydrated mix responses.** Mix catalogue responses and recommendation results now include `duration` and `imageUrl`, so consumers can render richer mix cards and detail views without extra lookups.

---

## What's new in v1.6

- **Permanent catalog cache.** All catalog endpoints are now served from an in-memory cache with no fixed TTL — data is always fast, never stale from a user's perspective.
- **Push-based cache invalidation.** A new `POST /api/catalog/flush` endpoint (Bearer token protected) lets an external cron job invalidate and immediately re-warm the cache the moment a new mix is uploaded to SoundCloud. Changsta.com always sees fresh data without paying the RSS + blob fetch cost on user requests.
- **Cache warm-up on startup.** The catalog is pre-loaded when the API process starts, so the first request after a deploy or restart is served instantly rather than cold.
- **24-hour fallback TTL.** If the flush endpoint is never called, the cache self-refreshes after 24 hours as a safety net.

---

## What's new in v1.5

- **Model switched to `gpt-4.1-mini`.** Default OpenAI model updated for better cost/quality balance on recommendation generation.
- **Simplified AI response contract.** `promptId` removed from the AI response — the server controls prompt versioning internally, reducing token overhead and ambiguity in the model output.
- **Tighter evidence matching.** Anchor validation is now stricter, reducing false-positive `why` matches where a quoted string appeared in an unrelated field.
- **Reduced prompt payload.** Per-mix metadata sent to the model is trimmed to evidence-relevant fields only, lowering token count per request.
- **Faster retries.** Exponential backoff delays tuned down — failed AI calls now recover noticeably faster on the second and third attempt.

---

## What's new in v1.4

- **Genre filter on `GET /api/catalog/mixes`.** Pass `?genre=dnb` (or any genre slug) to filter the mixes list to a single genre.
- **New `GET /api/catalog/genres` endpoint.** Returns a sorted, deduplicated, normalised list of all genres in the catalogue. Genre aliases (`deephouse → deep-house`) are applied consistently.
- **Larger catalog page sizes.** Maximum `pageSize` increased across all catalog endpoints, letting consumers retrieve more results per request.

---

## Project Goals

- Accept free-text queries — genre, artist, track name, mood, tempo, or any combination
- Return structured mix recommendations with a human-readable explanation and verifiable evidence
- Guarantee strict JSON contract compliance
- Prevent hallucinated data by validating every evidence anchor against real metadata
- Support CI/CD with environment-based deployments (dev, qa, prod)
- Be cloud deployable via Infrastructure as Code

---

## Why I Built This

I'm a long-time DJ and music collector, and over the years I've accumulated a large catalog of mixes and tracks across genres such as Drum & Bass, Breakbeat, House, UK Garage, and UK Bass.

Finding the right mix for a specific mood, tempo, or artist often requires manually scanning tracklists and remembering which mixes contain certain sounds or DJs. I built this project to explore whether a hybrid recommendation engine could solve that problem.

The goal of the system is to allow natural language queries such as:

- `"dark rolling dnb around 174 bpm"`
- `"something with Calibre or atmospheric liquid"`
- `"breakbeat with a rave vibe"`

The API interprets these queries, filters a catalog of mixes sourced from SoundCloud RSS feeds, and then uses a combination of deterministic ranking and AI reasoning to recommend the best matches.

This project serves two purposes:

- **Practical tool** – helping me and others rediscover mixes based on musical characteristics.
- **Engineering experiment** – exploring how traditional backend architecture can integrate AI reasoning in a controlled, explainable way.

Rather than relying purely on AI, the system combines deterministic filtering with AI-assisted interpretation to keep recommendations predictable, efficient, and transparent.

---

## How It Works

The API feeds up to 200 mixes from a persistent Azure Blob Storage catalog (supplemented by the live SoundCloud RSS feed) into a single OpenAI ChatCompletion call, with a structured prompt that:

1. **Decomposes the query** into signals — genre, artist/track, mood, or tempo — and prioritises the right metadata fields accordingly
2. **Generates a `reason`** per result: a free-text 1-2 sentence explanation written by the AI (creative, not validated)
3. **Generates `why` anchors** per result: 1-4 quoted strings copied verbatim from the mix's own metadata (deterministic, server-validated)

If validation fails the AI response is retried up to 3 times with exponential backoff before returning a 503.

---

## Example

**Request:**

```json
POST /api/mixes/recommend
{
  "question": "dark rolling dnb with Calibre",
  "maxResults": 3
}
```

**Response:**

```json
{
  "results": [
    {
      "mixId": "tag:soundcloud,2010:tracks/2047124840",
      "title": "Mycelium Epiphany",
      "url": "https://soundcloud.com/changsta/mycelium-epiphany",
      "reason": "This dark, rolling dnb set features Calibre and sits in the 172-174 BPM range, matching both the mood and artist you asked for.",
      "why": [
        "\"dnb\"",
        "\"dark\"",
        "\"Calibre - Pillow Dub\""
      ],
      "confidence": 0.92
    }
  ],
  "clarifyingQuestion": null,
  "maxResultsApplied": 3
}
```

Every `why` anchor exists verbatim in that mix's own genre, energy, mood, BPM, intro text, or tracklist — server-validated before returning.

---

## Query Types

| Query type | Example | Signal prioritised |
|---|---|---|
| Genre | `"dnb mixes"` | `genre` field |
| Artist / track | `"anything with Calibre"` | `tracklist` |
| Mood | `"something dark and heavy"` | `moods`, then `energy`, then `intro` |
| Tempo | `"fast, around 170 bpm"` | `bpm` |
| Mixed | `"dark dnb with Noisia around 174bpm"` | All dimensions |

---

## Validation Rules

Before any result is returned:

- Only allowed JSON properties are accepted (strict allowlist — `title` and `url` are rejected if present)
- `mixId` must exist in the server-side catalogue
- `reason` must be present and ≤ 300 characters
- `why` must contain 1–4 strings, each a quoted anchor verified against the mix's own metadata
- Each anchor must appear verbatim in: intro text, genre, energy, a mood token, BPM range, or a tracklist line
- `confidence` must be 0–1
- `clarifyingQuestion` must be null

---

## API Reference

### `POST /api/mixes/recommend`

| Field | Type | Required | Notes |
|---|---|---|---|
| `question` | string | yes | Natural language query. Min 1, max 2000 characters. |
| `maxResults` | integer | no | Default `3`, min `1`, max `20`. Silently clamped if out of range. |

**Response fields:**

| Field | Type | Notes |
|---|---|---|
| `results[].mixId` | string | Stable SoundCloud track ID |
| `results[].title` | string | Mix title, verbatim from catalogue |
| `results[].url` | string | SoundCloud URL, verbatim from catalogue |
| `results[].duration` | string\|null | Mix duration from catalogue RSS metadata |
| `results[].imageUrl` | string\|null | Mix artwork URL from catalogue RSS metadata |
| `results[].reason` | string | AI-written explanation of the match |
| `results[].why` | string[] | 1–4 evidence anchors, each verified against mix metadata |
| `results[].confidence` | float | 0.0–1.0 |
| `clarifyingQuestion` | string\|null | Non-null only when `question` is empty or whitespace |
| `maxResultsApplied` | integer | The effective maxResults used after clamping |

`results` may be an empty array on `200` — this means the AI found no catalogue mixes with sufficient evidence to match the query, or all retry attempts were exhausted. A `503` means the upstream HTTP connection to the AI service failed entirely.

---

### `GET /api/catalog`

Returns a paginated `CatalogPage<GenreEntry>` of the merged catalog (blob + RSS, up to 200 mixes), grouped by genre with artists and tracks nested inside. Useful for verifying which mixes the AI can see.

**Query parameters:** `?genre=dnb` (optional filter), `?page=1`, `?pageSize=20` (max 200)

---

### `GET /api/catalog/mixes`

Returns a paginated `CatalogPage<Mix>` of all mixes ordered by publish date descending.

**Query parameters:** `?genre=dnb` (optional filter), `?page=1`, `?pageSize=20` (max 200)

---

### `GET /api/catalog/genres`

Returns a sorted, deduplicated, normalised list of all genre slugs present in the catalogue (e.g. `dnb`, `ukg`, `deep-house`). Genre aliases are resolved before deduplication.

---

### `GET /api/catalog/artists`

Returns all artist names in a single unpaginated response, sorted alphabetically. No query parameters.

**Response:** `{ "artists": ["Anu", "Bicep", "Break", "Calibre", ...] }`

---

### `GET /api/catalog/tracks`

Returns a paginated `CatalogPage<TrackSummary>` of all tracks, deduplicated, with recurrence count and genres seen.

**Query parameters:** `?page=1`, `?pageSize=20` (max 200)

---

### `GET /api/catalog/artists/{name}/mixes`

Returns a paginated `CatalogPage<Mix>` of all mixes containing a track by the named artist.

**Query parameters:** `?page=1`, `?pageSize=20` (max 200)

---

### `GET /health`

Returns `Healthy` when the process is running. Used by Azure App Service for health probes.

---

## Tech Stack

- .NET 10 / ASP.NET Core Web API
- OpenAI SDK v2.1.0 (ChatCompletion)
- System.ServiceModel.Syndication (RSS parsing)
- Azure Blob Storage (persistent mix catalog — Azure.Storage.Blobs 12.24)
- Azure Managed Identity (DefaultAzureCredential in production)
- OpenTelemetry / Azure Monitor (workspace-based Application Insights per environment)
- IMemoryCache (1-hour merged catalog TTL)
- ASP.NET Core rate limiting (10 req/min per IP)
- System.Text.Json
- NUnit 4 + FluentAssertions
- GitHub Actions
- Azure App Service (F1 free tier for dev/qa, B1 for prod) + Azure Storage Account (Standard_LRS)
- Bicep (IaC)

---

## CI/CD Pipeline

### CI

Triggered on push to `develop` or `main`, and on pull requests.

Steps: restore → build → test → publish → upload artifact

### CD

| Environment | Trigger |
|---|---|
| Dev | Auto-deploy on CI success for `develop` |
| QA | Manual |
| Prod | Manual |

Uses OIDC authentication with Azure Service Principal and environment-scoped secrets.

---

## SoundCloud Integration

### RSS Feed Limitations

The API pulls mix metadata from your SoundCloud profile via the public RSS feed. However, **the RSS feed does not guarantee a full pull of all mixes** — SoundCloud typically exposes only the most recent 20–50 tracks, regardless of how many you have uploaded. For large catalogs this means a significant portion of your mixes will not appear in the RSS feed at all.

For example, with 100+ mixes uploaded, the RSS feed may return only ~50. The remaining mixes must be seeded manually into the Azure Blob Storage catalog (see [Seeding the Catalog](#seeding-the-catalog) below). Once the initial load is done, the API will automatically discover and persist any new mixes going forward on each cache refresh.

---

### RSS Feed URL

The RSS URL requires your **numeric SoundCloud user ID**, not your username slug. You can find it by visiting your SoundCloud profile and checking the RSS feed URL format:

```
https://feeds.soundcloud.com/users/soundcloud:users:YOUR_NUMERIC_ID/sounds.rss
```

Replace `YOUR_NUMERIC_ID` with your actual numeric ID (e.g. `41105`). You can find this ID by inspecting network requests on your SoundCloud profile page or using a tool like [this SoundCloud ID finder](https://www.soundcharts.com/blog/soundcloud-user-id).

---

### Mix Description Format

All structured metadata is extracted from the SoundCloud track description. Each mix description must follow this three-part format exactly:

**1. Short intro**

A 1–2 sentence description of the mix's feel, sound, and progression. This text is indexed and used as an evidence anchor for AI reasoning (`why` fields).

**2. Tracklisting**

Must be headed with the exact label `Tracklisting` (no colon) on its own line, followed by one track per line in `Artist - Title` format:

```
Tracklisting
Higgo - 3K (Instrumental)
Pa Salieu - Belly (Bakey Edit)
Gorgon City - 5AM At Bagleys (Extended Mix)
```

Every track line is stored individually and can be used as a verbatim evidence anchor during recommendation validation. Lines that do not match `Artist - Title` format are skipped.

**3. JSON metadata snippet**

A single line at the bottom of the description using the `[changsta:mix:v1 {...}]` tag:

```
[changsta:mix:v1 {"genre":"ukg","energy":"high","bpm":[132,140],"moods":["groovy","driving","dark","warehouse","energetic"]}]
```

| Property | Type | Description |
|---|---|---|
| `genre` | string | Primary genre tag (e.g. `"dnb"`, `"ukg"`, `"breakbeat"`, `"house"`) |
| `energy` | string | Energy level: `"low"`, `"medium"`, `"high"` |
| `bpm` | [int, int] | BPM range as a two-element array, e.g. `[132, 140]` |
| `moods` | string[] | 2–5 mood tokens describing the vibe (e.g. `"dark"`, `"driving"`, `"warehouse"`, `"liquid"`) |

If the JSON snippet is missing or malformed, the mix will still appear in the catalog but without structured metadata, which significantly reduces the quality of AI recommendations for that mix.

**Full example:**

```
The Flex Mix 5 starts off polite with that UKG swing, then gradually heads towards warehouse territory

Tracklisting
Higgo - 3K (Instrumental)
Pa Salieu - Belly (Bakey Edit)
Gorgon City - 5AM At Bagleys (Extended Mix)
Shermanology, Champion - Badder
THIRTZY - Chit Chat
Ed Case, Ms Dynamite - Hype (Ed Case Re-Fix) (Dirty) 1B 137
Prozak - Yush
Eloq, TALONS - 4 Million (Original Mix)
Bodhi - T.O.P.
MC DT, Silva Bumpa - Doin' It feat. MC DT (Original Mix)
Gorgon City, Interplanetary Criminal - Contact (Extended)
Andre Zimmer - Watch Diss (Original Mix)
DJ Hybrid - Soldier Original Mix
Hirobbie - Taste (Original Mix)

[changsta:mix:v1 {"genre":"ukg","energy":"high","bpm":[132,140],"moods":["groovy","driving","dark","warehouse","energetic"]}]
```

---

### Seeding the Catalog

For mixes that fall outside the RSS window, prepare a JSON document matching the `MixCatalogDocument` schema and upload it as `catalog.json` to the `mix-catalog` container in Azure Blob Storage. Once uploaded, the API will merge blob and RSS data on the next cache refresh and persist any new RSS discoveries back to blob automatically.

## Running Locally

### 1. Install prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js](https://nodejs.org/) (for Azurite via npm) or [Docker](https://www.docker.com/)

### 2. Install and start Azurite

Azurite is a local Azure Storage emulator. It replaces the real Azure Storage account during local development.

**Install (once):**

```
npm install -g azurite
```

Or via Docker:

```
docker run -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite
```

**Start (each session):**

```
azurite --silent --location /tmp/azurite --debug /tmp/azurite-debug.log
```

The blob service listens on `http://127.0.0.1:10000` by default.

---

### 3. Configure secrets

Use user secrets (recommended — keeps secrets out of source control):

```bash
dotnet user-secrets set "OpenAI:ApiKey" "your-key-here" --project Changsta.Ai.Interface.Api
dotnet user-secrets set "OpenAI:Model" "gpt-4.1-mini" --project Changsta.Ai.Interface.Api
dotnet user-secrets set "SoundCloud:RssUrl" "https://feeds.soundcloud.com/users/soundcloud:users:YOUR_NUMERIC_ID/sounds.rss" --project Changsta.Ai.Interface.Api
dotnet user-secrets set "Azure:BlobCatalog:ConnectionString" "UseDevelopmentStorage=true" --project Changsta.Ai.Interface.Api
```

`UseDevelopmentStorage=true` is the well-known connection string that points to Azurite. No extra configuration is needed — the app creates the `mix-catalog` container automatically on first write.

---

### 4. Build and run

```bash
dotnet restore
dotnet build soundcloud-ai-mix-recommender-api.sln
dotnet run --project Changsta.Ai.Interface.Api
```

On first request the app fetches the RSS feed and writes the initial `catalog.json` blob to Azurite. Subsequent requests within the 1-hour cache window are served from memory.

Once running, the Swagger UI is available at `http://localhost:<port>/swagger` (port shown in terminal output).

To inspect or edit the blob directly, use [Azure Storage Explorer](https://azure.microsoft.com/en-us/products/storage/storage-explorer/) and connect to `http://127.0.0.1:10000` with the Azurite account key (`devstoreaccount1` / `Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==`).

---

## Repo Layout

```
Changsta.Ai.Interface.Api/          # Controllers, middleware, DI, app config
Changsta.Ai.Core/                   # Domain models and DTOs
Changsta.Ai.Core.Contracts/         # Layer boundary interfaces
Changsta.Ai.Core.BusinessProcesses/ # Orchestration and use case logic
Changsta.Ai.Infrastructure.Services.Ai/          # OpenAI integration, prompt building, retry
Changsta.Ai.Infrastructure.Services.SoundCloud/  # RSS ingestion and parsing
Changsta.Ai.Infrastructure.Services.Azure/       # Blob persistence and Azure wiring
Changsta.Ai.Tests.Unit/             # NUnit + FluentAssertions
infra/                              # Bicep IaC templates
```

---

## Author

Christophe Chang
Senior .NET Architect
London, UK
