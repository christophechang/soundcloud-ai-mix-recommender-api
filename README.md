# SoundCloud Mix Recommender API

A production-ready **.NET 10 Web API** that recommends SoundCloud DJ mixes using AI, balancing creative relevance with strict server-side validation to prevent hallucination.

The guiding principle: **`reason` is creative; `why` is evidence. Both are required. Only `why` is verified against catalogue data.**

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

“dark rolling dnb around 174 bpm”
“something with Calibre or atmospheric liquid”
“breakbeat with a rave vibe”

The API interprets these queries, filters a catalog of mixes sourced from SoundCloud RSS feeds, and then uses a combination of deterministic ranking and AI reasoning to recommend the best matches.

This project serves two purposes:

- **Practical tool** – helping me and others rediscover mixes based on musical characteristics.
- **Engineering experiment** – exploring how traditional backend architecture can integrate AI reasoning in a controlled, explainable way.

Rather than relying purely on AI, the system combines deterministic filtering with AI-assisted interpretation to keep recommendations predictable, efficient, and transparent.

---

## How It Works

The API feeds up to 100 mixes from a persistent Azure Blob Storage catalog (supplemented by the live SoundCloud RSS feed) into a single OpenAI ChatCompletion call, with a structured prompt that:

1. **Decomposes the query** into signals — genre, artist/track, mood, or tempo — and prioritises the right metadata fields accordingly
2. **Generates a `reason`** per result: a free-text 1-2 sentence explanation written by the AI (creative, not validated)
3. **Generates `why` anchors** per result: 1-4 quoted strings copied verbatim from the mix's own metadata (deterministic, server-validated)

If validation fails the AI response is retried up to 3 times before returning an empty result set.

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
  "clarifyingQuestion": null
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

- Only allowed JSON properties are accepted (strict allowlist)
- `mixId` must exist in the server-side catalogue
- `title` and `url` must match canonical catalogue values exactly
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
| `question` | string | yes | Natural language query |
| `maxResults` | integer | no | Default `3`, min `1`, max `20` |

**Response fields:**

| Field | Type | Notes |
|---|---|---|
| `results[].mixId` | string | Stable SoundCloud track ID |
| `results[].title` | string | Mix title, verbatim from catalogue |
| `results[].url` | string | SoundCloud URL, verbatim from catalogue |
| `results[].reason` | string | AI-written explanation of the match |
| `results[].why` | string[] | 1–4 evidence anchors, each verified against mix metadata |
| `results[].confidence` | float | 0.0–1.0 |
| `clarifyingQuestion` | string\|null | Non-null only when `question` is blank |

`results` may be an empty array on `200` — this means the AI found no catalogue mixes with sufficient evidence to match the query.

---

### `GET /api/catalog`

Returns the full merged catalog (blob + RSS, up to 200 mixes) as a JSON array of mix objects. Useful for:

- Exporting the current catalog to seed new mixes into blob storage
- Verifying which mixes the AI can see

---

## Tech Stack

- .NET 10 / ASP.NET Core Web API
- OpenAI SDK v2.1.0 (ChatCompletion)
- System.ServiceModel.Syndication (RSS parsing)
- Azure Blob Storage (persistent mix catalog — Azure.Storage.Blobs 12.24)
- IMemoryCache (1-hour merged catalog TTL)
- ASP.NET Core rate limiting (10 req/min per IP)
- System.Text.Json
- NUnit 4 + FluentAssertions
- GitHub Actions
- Azure App Service (F1 free tier) + Azure Storage Account (Standard_LRS)
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

Every track line is stored individually and can be used as a verbatim evidence anchor during recommendation validation.

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

For mixes that fall outside the RSS window, use the included `catalog_import_template.csv` in the project root to prepare your back-catalog data, then upload the resulting JSON to the `catalog.json` blob in Azure Blob Storage. Once uploaded, the API will merge blob and RSS data on the next cache refresh and persist any new RSS discoveries back to blob automatically.

---

## TuneFinder Integration

This API is one half of a larger music discovery system called **[TuneFinder](https://www.soltechconsulting.co.uk/case-studies/tunefinder)**.

### What TuneFinder Does

TuneFinder automates DJ crate-digging. It aggregates ~2,000 new release candidates each week across Beatport, Juno Download, Bandcamp, Traxsource, Resident Advisor, and Subsurface Selections, then scores and ranks them to surface 15–20 relevant tracks — reducing what was several hours of manual discovery down to a five-minute weekly read.

### Role of This API

This API provides the **taste layer** that TuneFinder's scoring engine runs against. Specifically, it exposes:

- **A deduplicated track catalog** — every track that has appeared across all published mixes, sourced from the blob-backed catalog described above. TuneFinder uses this to build a known-track exclusion set, preventing recommendations for music already owned.
- **Artist affinity data** — artists appearing across multiple mixes carry higher recurrence weight (3× multiplier) in TuneFinder's scoring signals. The more frequently an artist appears in the mix catalog, the stronger their influence on new release rankings.

The `GET /api/catalog/tracks` endpoint serves this data directly.

### Discovery → Taste → Report

TuneFinder's weekly pipeline works in three stages:

1. **Discovery** — Six parallel source fetchers scrape new releases and deduplicate cross-source candidates
2. **Scoring** — Seven weighted signals are applied, with artist affinity (derived from this API's catalog) being the strongest predictor
3. **Report generation** — A two-stage LLM pipeline writes per-track reasoning (MiniMax M2.5 / Groq fallback) then formats the full Discord report in Claude Sonnet's voice

This API's role is entirely in stage 2 — it is the source of truth for what has already been played and who the trusted artists are.

---

## Running Locally

### 1. Install and start Azurite

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

### 2. Configure secrets

Add to `appsettings.Development.json` or user secrets:

```json
{
  "OpenAI": {
    "ApiKey": "your-key-here",
    "Model": "gpt-4o-mini"
  },
  "SoundCloud": {
    "RssUrl": "https://feeds.soundcloud.com/users/soundcloud:users:YOUR_ID/sounds.rss"
  },
  "Azure": {
    "BlobCatalog": {
      "ConnectionString": "UseDevelopmentStorage=true",
      "ContainerName": "mix-catalog",
      "BlobName": "catalog.json"
    }
  }
}
```

`UseDevelopmentStorage=true` is the well-known connection string that points to Azurite. No extra configuration is needed — the app creates the `mix-catalog` container automatically on first write.

To set via user secrets instead (recommended, keeps secrets out of source control):

```
dotnet user-secrets set "Azure:BlobCatalog:ConnectionString" "UseDevelopmentStorage=true" --project Changsta.Ai.Interface.Api
dotnet user-secrets set "OpenAI:ApiKey" "your-key-here" --project Changsta.Ai.Interface.Api
```

---

### 3. Run

```
dotnet run --project Changsta.Ai.Interface.Api
```

On first request the app fetches the RSS feed and writes the initial `catalog.json` blob to Azurite. Subsequent requests within the 1-hour cache window are served from memory.

Once running, the Swagger UI is available at `http://localhost:5059/swagger` (port may vary — check terminal output).

To inspect or edit the blob directly, use [Azure Storage Explorer](https://azure.microsoft.com/en-us/products/storage/storage-explorer/) and connect to `http://127.0.0.1:10000` with the Azurite account key (`devstoreaccount1` / `Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==`).

---

## Author

Christophe Chang
Senior .NET Architect
London, UK
