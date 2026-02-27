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

The API feeds up to 50 mixes from the SoundCloud RSS catalogue into a single OpenAI ChatCompletion call, with a structured prompt that:

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

## Tech Stack

- .NET 10 / ASP.NET Core Web API
- OpenAI SDK v2.1.0 (ChatCompletion)
- System.ServiceModel.Syndication (RSS parsing)
- IMemoryCache (1-hour RSS TTL)
- ASP.NET Core rate limiting (10 req/min per IP)
- System.Text.Json
- NUnit 4 + FluentAssertions
- GitHub Actions
- Azure App Service (F1 free tier)
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

Add to `appsettings.Development.json` or user secrets:

```json
{
  "OpenAI": {
    "ApiKey": "your-key-here",
    "Model": "gpt-4.1-mini"
  },
  "SoundCloud": {
    "RssUrl": "https://feeds.soundcloud.com/users/soundcloud:users:YOUR_ID/sounds.rss"
  }
}
```

Or via environment variables:

```
OpenAI__ApiKey=...
OpenAI__Model=gpt-4.1-mini
```

```
dotnet run --project Changsta.Ai.Interface.Api
```

---

## Author

Christophe Chang
Senior .NET Architect
London, UK
