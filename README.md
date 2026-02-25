# SoundCloud Mix Recommender API

A production-ready **.NET 10 Web API** that recommends SoundCloud DJ
mixes using AI while enforcing strict, deterministic JSON output and
server-side validation to minimise hallucination.

This project demonstrates controlled AI fuzziness: let the model rank
creatively, but enforce hard validation rules before returning anything
to clients.

------------------------------------------------------------------------

# Project Goals

-   Accept free-text user questions such as:

    > "Something to listen to while cooking"

-   Return structured mix recommendations.

-   Guarantee strict JSON contract compliance.

-   Prevent hallucinated data.

-   Ensure every explanation is traceable to real metadata.

-   Support CI/CD with environment-based deployments (dev, qa, prod).

-   Be cloud deployable via Infrastructure as Code.

------------------------------------------------------------------------

# High-Level Design

This API uses a **two-stage AI pipeline**:

## Stage A -- Creative Ranking

-   Semantic matching via embeddings.
-   Optional heuristic boosting.
-   Returns top candidate mix IDs.

## Stage B -- Strict Formatter

-   LLM generates final JSON.
-   Strict prompt rules enforce:
    -   JSON only
    -   No markdown fences
    -   No external knowledge
    -   Verbatim anchor usage
-   Output is validated server-side before being returned.  

------------------------------------------------------------------------

# Example Request

**Query:**

Something to listen to while cooking

**Response:**

``` json
{
  "results": [
    {
      "mixId": "tag:soundcloud,2010:tracks/2267079023",
      "title": "The Sunflower Mix 2",
      "url": "https://soundcloud.com/changsta/sunflower-mix-2",
      "why": [
        "warm, rolling grooves",
        "soulful bounce",
        "sunny"
      ],
      "confidence": 0.9
    }
  ],
  "clarifyingQuestion": null
}
```

Each `why` item must exist verbatim inside the mix's own metadata. 

------------------------------------------------------------------------

# Strict Validation Rules

Before returning results:

-   Only allowed JSON properties are accepted.
-   `mixId` must exist in server dataset.
-   `title` and `url` must match canonical values.
-   2--4 `why` anchors per result.
-   Each `why` must:
    -   Be quoted.
    -   Exist verbatim in intro, tags, or tracklist.
    -   Belong to the same mix block.
-   Confidence must be between 0 and 1.
-   `clarifyingQuestion` must be null.

If validation fails, the request is rejected.

------------------------------------------------------------------------

# Tech Stack

-   .NET 10
-   ASP.NET Core Web API
-   OpenAI ChatCompletion API
-   Embeddings API
-   System.Text.Json
-   NUnit
-   GitHub Actions
-   Azure App Service
-   Bicep

------------------------------------------------------------------------

# CI/CD Pipeline

## CI

Triggered on: - Push to `develop` - Push to `main` - Pull requests

Steps: - Restore - Build - Run tests - Publish API - Upload artifact -
Upload test results

Artifacts are immutable and used for deployments.

## CD

-   Dev: auto-deploy on successful CI for `develop`
-   QA: manual deployment
-   Prod: manual deployment

Uses OIDC authentication with Azure Service Principal and
environment-scoped secrets.

------------------------------------------------------------------------

# Running Locally

## Configure OpenAI

``` json
{
  "OpenAI": {
    "ApiKey": "your-key-here",
    "Model": "gpt-4.1-mini"
  }
}
```

Or via environment variables:

    OpenAI__ApiKey=...
    OpenAI__Model=gpt-4.1-mini

## Run

    dotnet run --project Changsta.Ai.Interface.Api

------------------------------------------------------------------------

# Project Status

Active development focused on strict-mode explainable recommendations
and CI/CD hardening.

------------------------------------------------------------------------

# Author

Christophe Chang\
Senior .NET Architect\
London, UK
