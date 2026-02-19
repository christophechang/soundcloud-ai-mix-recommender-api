\# SoundCloud Mix Recommender API



A .NET 10 Web API that recommends SoundCloud mixes using Microsoft

Semantic Kernel + OpenAI ChatCompletion, with strict JSON output rules

and server-side validation to minimise hallucinations.



\## Overview



This API accepts:



\-   A free-text user question\\

    Example:\\

    "Something to listen to while cooking"



\-   A collection of mixes containing:



    -   id

    -   title

    -   url

    -   intro/description text

    -   tags

    -   tracklist



It returns structured recommendations in strict JSON format:



{ "results": \\\[ { "mixId": "...", "title": "...", "url": "...", "why":

\\\["\\"anchor\\""\\], "confidence": 0.85 } \\], "clarifyingQuestion": null }



Each `why` item must be a verbatim anchor copied directly from that

mix's own metadata.



The design goal is controlled fuzziness: - Let the model choose what

fits. - Enforce that every justification is traceable to the provided

data.



---



\## Tech Stack



\-   .NET 10

\-   ASP.NET Core Web API

\-   Microsoft Semantic Kernel

\-   OpenAI ChatCompletion API

\-   System.Text.Json for strict schema validation



---



\## Architecture



\### 1. Prompt Construction



Each mix is converted into a structured block:



MIX\\

id: ...\\

title: ...\\

url: ...\\

intro: ...\\

tags: ...\\

tracklist: ...\\

END\_MIX



Strict rules are injected into the prompt:



\-   JSON output only

\-   No markdown fences

\-   No outside knowledge

\-   Every `why` string must be a quoted anchor copied verbatim from the

    same mix block

\-   2--4 anchors per result

\-   Schema must match exactly



---



\### 2. AI Call (Semantic Kernel)



The API:



\-   Builds a Kernel

\-   Resolves IChatCompletionService

\-   Sends structured prompt + system message

\-   Receives raw model output



---



\### 3. Normalisation Layer



The response is sanitised to handle common LLM quirks:



\-   Remove UTF-8 BOM

\-   Strip \\`\\\\`\\` code fences

\-   Extract first JSON object if extra text appears



---



\### 4. Validation Layer



Before returning results:



\-   Enforce top-level allowed JSON properties

\-   Enforce allowed properties per result

\-   Validate mixId exists

\-   Validate title and url match canonical server data

\-   Validate 2--4 why entries

\-   Validate each why entry is:

    -   A quoted anchor

    -   Found verbatim in intro, tags, or tracklist of the same mix



If any rule fails, the request is rejected.



---



\## Example Use Case



Query:



Something to listen to while cooking



Response:



{ "results": \\\[ { "mixId": "tag:soundcloud,2010:tracks/2267079023",

"title": "The Sunflower Mix 2", "url":

"https://soundcloud.com/changsta/sunflower-mix-2", "why": \\\[ "\\"warm,

rolling grooves\\"", "\\"soulful bounce\\"", "\\"sunny\\"" \\], "confidence":

0.9 } \\], "clarifyingQuestion": null }



---



\## Running Locally



1\.  Set OpenAI configuration in appsettings.json:



"OpenAI": { "ApiKey": "your-key-here", "Model": "gpt-4.1-mini" }



2\.  Run the API:



dotnet run



---



\## Status



Work in progress.\\

Currently focused on strict-mode explainable recommendations with zero

hallucination tolerance.



---



\## Future Ideas



\-   Fuzzy mode with ranked scoring

\-   Embedding-based semantic search

\-   RAG with persistent vector store

\-   SoundCloud metadata ingestion pipeline

\-   Public-facing demo endpoint

