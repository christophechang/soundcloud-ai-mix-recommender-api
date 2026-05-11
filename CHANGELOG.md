# Changelog

Notable changes to the SoundCloud Mix Recommender API.

## v1.21

- **Duplicate recommendation mix ID fix.** `AiRecommendationResponseValidator` now deduplicates mix IDs returned by the AI before validation, preventing duplicate entries from surfacing in recommendation results.

## v1.20

- **Diagnostics error insights endpoint.** New `GET /api/diagnostics/errors?hours={n}` endpoint (Bearer-authenticated, same secret as catalog flush) queries App Insights via Log Analytics and returns recent exception summaries. `hours` accepts 1–168 (default 24). Infra: App Service identity granted Log Analytics Reader role; `Azure__LogAnalytics__WorkspaceId` wired via Bicep.

## v1.19

- **Mix titles autocomplete endpoint.** New read-only `GET /api/catalog/mixes/titles` endpoint returns a flat array of `{ title, slug }` objects for all catalog mixes, sorted newest-first. Intended for lightweight autocomplete and client-side search. No auth required. Response includes `Cache-Control: max-age=3600, public`.

## v1.18

- **Mix deletion by slug.** `DELETE /api/catalog/mixes?slug=<slug>` now matches by the SoundCloud URL slug (e.g. `jungle-session-1`) rather than the full RSS GUID, avoiding URL encoding issues with special characters in route parameters.

## v1.17

- **Mix deletion endpoint.** New `DELETE /api/catalog/mixes/{id}` endpoint (Bearer-authenticated, same secret as catalog flush) removes a mix from the blob catalog by its stable RSS GUID. On success the cache is invalidated and rewarmed immediately — no separate flush needed. Returns 204 on success, 404 if no mix with that ID exists. Intended for manual curation when a mix is removed from SoundCloud and needs to be purged from the catalog without waiting for a full data migration.

## v1.16

- **OpenAI recommender responsibilities split.** `OpenAiMixRecommender` now focuses on orchestration, with query analysis, prompt construction, and AI response validation moved into dedicated internal components.
- **Release history moved out of the README.** Version notes now live in `CHANGELOG.md`, keeping the README focused on current project usage and behaviour.
- **Release workflow documented.** `CLAUDE.md` now defines the `release this version` flow for changelog updates, develop/main promotion, tagging, QA/Prod deployment, and GitHub release creation.

## v1.15

- **Tracklist preserved when RSS artist/title fields are swapped.** Legacy blob entries matched by tracklist during a permalink migration now tolerate artist and title fields appearing in reversed order in the RSS feed. The blob tracklist is also kept when a schema sync would have overwritten it with swapped entries, avoiding silent corruption of track attribution.

## v1.14

- **Permalink change handling.** When a SoundCloud mix URL changes, the catalog now detects the same track by its stable SoundCloud ID (`tag:soundcloud,2010:tracks/{id}`), transfers all computed metadata (genre, energy, BPM, moods, related mixes) to the new URL, and removes the orphaned old URL from the blob catalog. Previously the old URL would accumulate as dead weight and the new URL would lose all computed data.
- **Smart quote fix in schema parser.** The `[changsta:mix:v1 {...}]` JSON block parser now normalises Unicode curly quotes (`“`/`”`) to straight quotes before parsing. Fixes schema extraction failures when SoundCloud's description editor auto-converts quotation marks.

## v1.13

- **Mix detail by slug.** New endpoint `GET /api/catalog/mixes/{slug}` returns the full mix object for a given SoundCloud URL slug (e.g. `GET /api/catalog/mixes/fault-lines`). The slug is the last path segment of the mix's SoundCloud permalink — no transformation needed. Returns 404 if no matching mix is found. Served from the same 24-hour memory cache as all other catalog endpoints.

## v1.12

- **Related mixes on every catalog item.** Each mix returned by `GET /api/catalog/mixes` and `GET /api/catalog/artists/{name}/mixes` now includes a `relatedMixes` array of up to 6 slim references (title, URL, artwork URL), pre-computed and ranked by relevance. Scoring uses shared exact tracks (+15 each), shared tracklist artists (+8 each), same genre (+6), same energy (+3), shared moods (+2 each), and BPM range overlap (+1). Ties are broken by most recently published. Results are computed once at cache flush time, persisted to the blob catalog, and served from the 24-hour memory cache at zero read cost.

## v1.11

- **Live schema sync on cache flush.** Editing the `[changsta:mix:v1 {...}]` JSON block in a SoundCloud mix description and calling `POST /api/catalog/flush` now propagates the updated genre, energy, BPM, moods, and tracklist into the blob-backed catalog. Sync only fires when the description has changed and the updated description contains a valid schema block — mixes without the schema tag are unaffected.
- **Recommendation catalog expanded to 200 mixes.** The AI recommender now receives up to 200 catalog mixes (was 50), consistent with all other catalog endpoints. The recommender's own 100-mix prompt cap still applies, so token count is unchanged.
- **Tech house genre aliases added.** `tech house` and `tech-house` now normalise to `techno` in the genre normaliser, matching the existing alias table used in recommendation filtering.
- **AI response contract tightened.** `title` and `url` fields removed from the allowed AI response schema — the server always resolves these from the catalog. Any AI response containing these fields is now rejected rather than silently ignored, eliminating wasted tokens.
- **CORS fix for catalog flush.** Browser clients can now send `Authorization: Bearer` headers to `POST /api/catalog/flush` without hitting a CORS preflight failure.

## v1.10

- **Canonical genre normalization.** Catalogue sync now normalizes overlapping genre aliases such as `breaks`, `uk-bass`, `ukbass`, `deep house`, `drum and bass`, and `garage` into one canonical genre value before mixes are cached, stored, indexed, or returned.
- **Cleaner genre endpoints and filters.** `GET /api/catalog/genres`, catalogue grouping, mix filtering, track summaries, and recommendation genre filtering now use the same reusable normalizer, so aliases resolve consistently across API responses.
- **Legacy catalogue cleanup on sync.** Existing blob-backed catalogue entries are normalized during sync and written back when needed, while unknown genres are preserved in lowercase form and logged once per sync run for future mapping updates.

## v1.9

- **Legacy RSS metadata hydration.** SoundCloud RSS items without a `[changsta:mix:v1 ...]` schema block are now still read for metadata, allowing older mixes to pick up live title, description, duration, artwork, and publish-date updates.
- **Safe cover-art backfill.** Metadata-only RSS rows can refresh matching existing blob catalogue entries without being added as new uncurated catalogue mixes.
- **Cache flush improvements.** Flushing the catalogue cache now backfills cover URLs for legacy mixes that pre-date structured JSON metadata, while preserving curated genre, energy, BPM, moods, and tracklist data.

## v1.8

- **Lightweight artist name endpoint.** `GET /api/catalog/artists` now returns a single unpaginated response with all artist names sorted alphabetically — `{ "artists": ["Anu", "Bicep", ...] }`. No track data, no pagination params. Optimised for autocomplete use cases.

## v1.7

- **RSS duration and artwork ingestion.** SoundCloud RSS processing now captures `itunes:duration` and `itunes:image` for each mix and stores them in the blob-backed catalogue cache as `duration` and `imageUrl`.
- **Blob cache metadata refresh.** Existing cached mixes are refreshed with live RSS title, description, publish date, duration, and artwork metadata while preserving curated genre, energy, BPM, moods, and tracklist data.
- **Hydrated mix responses.** Mix catalogue responses and recommendation results now include `duration` and `imageUrl`, so consumers can render richer mix cards and detail views without extra lookups.

## v1.6

- **Permanent catalog cache.** All catalog endpoints are now served from an in-memory cache with no fixed TTL — data is always fast, never stale from a user's perspective.
- **Push-based cache invalidation.** A new `POST /api/catalog/flush` endpoint (Bearer token protected) lets an external cron job invalidate and immediately re-warm the cache the moment a new mix is uploaded to SoundCloud. Changsta.com always sees fresh data without paying the RSS + blob fetch cost on user requests.
- **Cache warm-up on startup.** The catalog is pre-loaded when the API process starts, so the first request after a deploy or restart is served instantly rather than cold.
- **24-hour fallback TTL.** If the flush endpoint is never called, the cache self-refreshes after 24 hours as a safety net.

## v1.5

- **Model switched to `gpt-4.1-mini`.** Default OpenAI model updated for better cost/quality balance on recommendation generation.
- **Simplified AI response contract.** `promptId` removed from the AI response — the server controls prompt versioning internally, reducing token overhead and ambiguity in the model output.
- **Tighter evidence matching.** Anchor validation is now stricter, reducing false-positive `why` matches where a quoted string appeared in an unrelated field.
- **Reduced prompt payload.** Per-mix metadata sent to the model is trimmed to evidence-relevant fields only, lowering token count per request.
- **Faster retries.** Exponential backoff delays tuned down — failed AI calls now recover noticeably faster on the second and third attempt.

## v1.4

- **Genre filter on `GET /api/catalog/mixes`.** Pass `?genre=dnb` (or any genre slug) to filter the mixes list to a single genre.
- **New `GET /api/catalog/genres` endpoint.** Returns a sorted, deduplicated, normalised list of all genres in the catalogue. Genre aliases (`deephouse → deep-house`) are applied consistently.
- **Larger catalog page sizes.** Maximum `pageSize` increased across all catalog endpoints, letting consumers retrieve more results per request.
