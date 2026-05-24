# Changelog

Notable changes to the SoundCloud Mix Recommender API.

## v1.39

- **Station strapline field.** Each station in the radio API response now includes a `strapline` string (e.g. `"For the Breaks Headz"`).
- **Station genres field.** Each station now exposes its programmed `genres` string array, sourced directly from the scheduler definitions.

## v1.38

- **Station slug field.** Each station in the radio API response now includes a `slug` property (`crucial-fm`, `origin-fm`, `killa-fm`) for use in URLs and routing.
- **Internal NowSpinning rename.** Internal `NowSpinning` namespace and `NowSpinningMixMapper` class renamed to `Radio`. No API contract changes.

## v1.37

- **Radio mix intro text.** `RadioMixVm` now includes an optional `intro` field, sourced from the mix's `Intro` property. Exposed through all radio station endpoints.

## v1.36

- **Station rename.** Renamed radio station "Crucial" to "Crucial FM".

## v1.35

- **Station rename.** Updated radio station IDs and names: `touchdown-fm` → `140` (Crucial), `deep-signal-fm` → `4x4` (Origin FM), `jungle-pressure` → `170` (Killa FM). Frequencies, descriptions, genres, and schedule data unchanged.

## v1.34

- **Radio stations API.** Replaced the Now Spinning endpoints with a multi-station radio schedule API. `GET /api/catalog/radio/stations` returns the full schedule across all stations; each station owns a genre cluster with BPM offsets and energy thresholds. The scheduler builds per-station hour slots using a slot scorer that applies energy auditing and clustering penalties. A schedule validator enforces all scheduling rules before the response is returned. Stations that cannot produce a valid schedule return 503.
- **Station-relative BPM and energy scoring.** Slot selection uses station-anchored BPM ranges and an energy audit pass to penalise consecutive high-energy or low-energy clusters, ensuring each station's schedule has audible dynamics.
- **Threshold fallback.** When no mix clears the primary energy threshold, the scheduler relaxes to a fallback threshold rather than failing, preventing empty slots on sparse catalogs.

## v1.33

- **Related mix scoring improvements.** Genre weight raised (6 → 10) to act as a firmer anchor. BPM matching is now graduated (tight < 3 BPM diff = +3, loose < 8 BPM = +2, range overlap = +1) instead of a flat +1 binary. Warmth proximity scoring added (+2 for delta < 0.2, +1 for delta < 0.5) using the existing `Warmth` field that was previously unused.

## v1.32

- **Weekly mix rotation guarantee.** The radio slot picker now uses a week-seeded Fisher-Yates shuffle so each day of a Unix week maps to a distinct pool position. Within any 7-day window falling in the same week, the same mix will not repeat. The week resets to a fresh shuffle each Sunday-aligned Unix week boundary.

## v1.31

- **Day-based radio rotation.** The seeded pick for each slot now advances once per calendar day instead of once per hour, ensuring mixes cycle across the full pool day-to-day and avoiding repeated surfacing of the same mixes on consecutive days.

## v1.30

- **Rename Now Spinning routes to Radio.** `GET /api/catalog/now-spinning` is now `GET /api/catalog/radio` and `GET /api/catalog/now-spinning/program` is now `GET /api/catalog/radio/program`.
- **Mix catalog total count header.** `GET /api/catalog/mixes` now returns `X-Total-Count` response header containing the total number of matching mixes before pagination.

## v1.29

- **Now Spinning image URL.** `GET /api/catalog/now-spinning` and `GET /api/catalog/now-spinning/program` now include `imageUrl` in each mix response, sourced from the SoundCloud artwork URL stored in the catalog.

## v1.28

- **Remove skip from Now Spinning endpoints.** Removed the `skip` query parameter and `skipsIgnored` response field from both `GET /api/catalog/now-spinning` and `GET /api/catalog/now-spinning/program`. Skip was a recommendation-style concept that conflicted with the broadcast-schedule model of these endpoints; the seeded hourly pick already provides deterministic, repeatable selection without per-user state.

## v1.27

- **Now Spinning program endpoint.** New `GET /api/catalog/now-spinning/program` returns all five mood lanes (default, darker, warmer, slower, faster) in a single call. Now-playing picks are guaranteed unique across lanes via a shared exclusion set drawn in shuffled order per UTC hour. Response is cached in-memory until the next hour boundary.
- **Refactor: shared draw and mapping helpers.** Extracted `NowSpinningDrawer` and `NowSpinningMixMapper` to eliminate duplicated logic between the two Now Spinning use cases and controllers.

## v1.26

- **Now Spinning mood lane uniqueness.** Each `moodLean` value (`darker`, `warmer`, `slower`, `faster`) now produces a deterministically distinct mix selection per hour. Mood lean is factored into the seeded pick so different mood lanes never collide on the same mix at the same time slot.

## v1.25

- **Now Spinning endpoint.** New `GET /api/catalog/now-spinning` returns one pre-scored mix for the current time slot plus a configurable schedule of upcoming slots. Scoring assigns every mix to one of 24 pools (6 time slots × 4 day buckets) based on BPM, warmth, and energy. Pick is seeded by UTC hour + skip list so all listeners in the same state hear the same mix. Supports `moodLean` filtering (`darker`, `warmer`, `slower`, `faster`), `skip` exclusions, and `utcOffsetMinutes` for local time resolution. Graceful fallback: ignores skip → ignores lean → adjacent slot → 503.

## v1.24

- **Fix: query-word mood anchors no longer drop mixes.** The AI prompt now explicitly forbids copying words from the user query into `why` anchors (rule 10c). Mixes are no longer silently dropped when the model fabricates a mood like `driving` from the user's query text. The validator remains strict — anchors must come verbatim from the MIX block's structured fields. Adds a regression test covering a query-derived anchor absent from the mix's moods list.

## v1.23

- **AI mood weight enrichment.** After RSS parse and catalog merge, unknown mood tags are automatically scored by AI in a single batch call using all existing weights as context, ensuring scores are relative to the established -2.0 to +2.0 scale. Results are persisted to a blob sidecar (`mood_weights_enriched.json`) and merged at runtime with the human-curated baseline. No AI call is made if all moods are already known.
- **Expanded mood weight baseline.** 34 new mood tags promoted from AI enrichment into `mood_weights.json`: `abstract`, `bass heavy`, `bassline`, `bouncy`, `breakbeat heritage`, `carnival`, `chaotic`, `cheeky`, `chunky`, `club`, `crate digger`, `emotive`, `ethereal`, `feel good`, `focused`, `future-facing`, `glowing`, `introspective`, `late night`, `minimal`, `moody`, `nostalgic`, `progressive`, `punchy`, `raw`, `rebellious`, `reflective`, `skippy`, `sound system`, `sunlit`, `urban`, `urgent`, `vocal`, `warped`.

## v1.22

- **Warmth scoring.** Each mix now carries a `warmth` score in [-1, 1] computed from mood weights (configurable via `config/mood_weights.json`) plus an energy nudge (high → colder, low → warmer). Scores are persisted to the blob catalog and recomputed on flush when weights or moods change.
- **Compass endpoint.** New `GET /api/catalog/compass` returns all mixes that have a warmth score as a slim `CompassEntry` array (slug, title, URL, image, genre, energy, BPM, warmth, moods, publishedAt). Response includes `Cache-Control: max-age=3600, public`. Intended for mood-based browsing and visualisation.

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
