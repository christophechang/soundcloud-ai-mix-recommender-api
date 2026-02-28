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
| `maxResults` | integer | no | Default `3`, min `1`, max `5` |

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

To inspect or edit the blob directly, use [Azure Storage Explorer](https://azure.microsoft.com/en-us/products/storage/storage-explorer/) and connect to `http://127.0.0.1:10000` with the Azurite account key (`devstoreaccount1` / `Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==`).

---

## Author

Christophe Chang
Senior .NET Architect
London, UK
