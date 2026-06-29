# Tracklist Cue Points & Multi-Format Parsing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Teach the tracklist parser to read line numbers and cue points (bracketed `[m:ss]` and bare `mm:ss`) in addition to the existing `Artist - Title` format, store each cue point as `cuePointSeconds` in the JSON catalogue, and surface it additively on every per-mix tracklist API response.

**Architecture:** A single unified per-line parser handles all formats by stripping an optional leading line number then an optional leading cue point (bracketed or bare) before the existing first-`" - "` split. The cue point is carried as a new optional `int? CuePointSeconds` on the `Track` domain type (excluded from equality). The hand-written blob converter is extended to read/write the field; the public API picks it up automatically because the three per-mix endpoints serialize the domain `Mix` via reflection.

**Tech Stack:** .NET 10, C# (block-scoped namespaces), NUnit 4 + FluentAssertions, System.Text.Json, `System.Text.RegularExpressions` (static `Regex` fields like `MixRecommendationQueryAnalyzer`; the patterns are anchored at `^` with bounded quantifiers, so no match-timeout/try-catch is needed — see Task 3 note).

## Global Constraints

- Build: `dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental`
- Test: `dotnet test soundcloud-ai-mix-recommender-api.sln --no-build`
- `Soltech.ruleset` governs StyleCop/CA analyzers. Notable rules set to **Error**: `SA1201` (members ordered by type), `SA1202` (ordered by access), `SA1204` (static before instance), `SA1601` (partial elements must be documented — **do not use `[GeneratedRegex]` partial methods**; use static `Regex` fields). `SA1600` is **None** (public members need no XML docs — match the file's existing no-doc style).
- Match the touched file's style: `string`/`int` aliases, `_camelCase` private instance fields, PascalCase static readonly fields, block-scoped namespaces, existing `ConfigureAwait(false)` usage.
- Tests must be deterministic — no live OpenAI/SoundCloud/Azure calls. Follow existing patterns in `Changsta.Ai.Tests.Unit/`. Parsing tests use NUnit `Assert.That`.
- Conventional commits. **No `Co-Authored-By` trailers.**
- Treat API routes, DTO names, and config keys as stable contracts. This change is purely **additive** (one new optional field); it must not rename or remove anything.
- Do not add NuGet dependencies. Do not change secrets/connection strings.

---

## Design decisions (locked during brainstorming)

1. **One field, `cuePointSeconds` (`int?`).** Seconds is the canonical, most-efficient representation for a media time-offset; the UI formats `mm:ss` for display and uses the number directly to position markers on the interactive timeline. No display string is stored.
2. **Excluded from `Track` equality.** Identity is artist + title. Verified safe against every consumer: `CatalogProjections.TrackSummaries` (recurrence `GroupBy`), `RelatedMixScorer.BuildTrackSet`, and `MixCatalogueMerger.SameTrack` all key on artist+title only.
3. **Cache writer omits the field when null** (keeps existing cached tracks byte-identical on rewrite); **the API emits `cuePointSeconds: null` when absent** (consistent with how the API already emits `intro: null`, `bpmMin: null`). Two layers, each internally consistent.
4. **Cue points surface only on the per-mix tracklist endpoints** (`GET /api/catalog/mixes`, `/mixes/{slug}`, `/artists/{name}/mixes`) — automatic via domain serialization. The cross-mix aggregates (`/api/catalog/tracks`, `/api/catalog` genre tree) are deliberately left unchanged: a track deduped across many mixes has no single cue point, and the genre tree's `tracks: string[]` cannot gain a field without a breaking shape change.
5. **Supported formats** (all reduce to: optional `N.` → optional cue → `Artist - Title`):
   - `Artist - Title` (existing — cue null)
   - `N. Artist - Title`
   - `N. [m:ss] Artist - Title`
   - `[m:ss] Artist - Title`
   - `mm:ss Artist - Title` (bare cue)
   - plus `h:mm:ss`, mixed lines, and `[...]`/`(...)` annotations preserved verbatim in titles.
6. **Bare-cue disambiguation guard:** a bare (unbracketed) leading timestamp is treated as a cue point only if the remainder still contains `" - "`. This protects an artist literally named like a timestamp, e.g. `20:20 - Vision` → artist `20:20`, title `Vision`, cue null.
7. **Cue points are per-track and independent.** Each line is parsed on its own, so a single tracklist may carry cue points on **all, some, or none** of its tracks (mixed tracklists are normal). A track without a cue point has `CuePointSeconds == null` end-to-end: the parser leaves it null, the cache `Write` omits the field, and the API emits `cuePointSeconds: null`. No tracklist-level "has cue points" flag exists or is needed.

---

## File map

### Modified files

| File | Change |
|---|---|
| `Changsta.Ai.Core/Domain/Track.cs` | Add `public int? CuePointSeconds { get; init; }` (after `Title`). Equality/`GetHashCode` unchanged. |
| `Changsta.Ai.Core/Parsing/MixDescriptionParser.cs` | Marker detection tolerates a trailing colon (`Tracklist:`). |
| `Changsta.Ai.Infrastructure.Services.SoundCloud/Parsing/TracklistExtractor.cs` | Strip optional leading line number + cue point per line; populate `CuePointSeconds`. |
| `Changsta.Ai.Infrastructure.Services.Azure/Catalogue/BlobMixCatalogueRepository.cs` | `JsonOptions` `private` → `internal` (test seam); converter `Read`/`Write` handle `cuePointSeconds`. |

### New files

| File | Responsibility |
|---|---|
| `Changsta.Ai.Tests.Unit/Domain/TrackTests.cs` | Equality ignores cue point; default null. |
| `Changsta.Ai.Tests.Unit/Catalogue/TrackListJsonConverterTests.cs` | Blob converter round-trip + backward compatibility. |
| `Changsta.Ai.Tests.Unit/Api/TrackApiSerializationTests.cs` | Locks the additive API wire shape (`cuePointSeconds`, camelCase). |

### Modified test files

| File | Change |
|---|---|
| `Changsta.Ai.Tests.Unit/Parsing/TracklistExtractorTests.cs` | Add cases for every new format + edge cases. |
| `Changsta.Ai.Tests.Unit/Parsing/MixDescriptionParserTests.cs` | Add trailing-colon marker cases. |

### Untouched on purpose
`CatalogProjections.cs`, `MixCatalogController.cs`, `RelatedMixScorer.cs`, `MixCatalogueMerger.cs`, `MixPromptBuilder.cs`, `Docs/catalog.json`. Existing cached blobs deserialize unchanged (absent cue → null).

**How cue points reach existing mixes (no merger change):** on re-ingest, `MixCatalogueMerger` rebuilds `Tracklist` from the freshly parsed description under its existing `syncSchema`/`syncTracklist` branch, so a re-parsed tracklist carries cue points. `SameTrack`/`SameTracklist` compare artist+title only — intentionally cue-agnostic — so a tracklist that newly gained cue points is still treated as "the same" and written through. This is the desired path; the converter's omit-when-null `Write` keeps null-cue tracks byte-identical on rewrite.

---

## Task 1: `Track.CuePointSeconds` (domain field, excluded from equality)

**Files:**
- Modify: `Changsta.Ai.Core/Domain/Track.cs`
- Test: `Changsta.Ai.Tests.Unit/Domain/TrackTests.cs` (create)

**Interfaces:**
- Produces: `Track { string Artist; string Title; int? CuePointSeconds }`. Equality and `GetHashCode` cover **only** `Artist` + `Title`. Consumed by Tasks 3, 4, 5.

- [ ] **Step 1.1: Write the failing tests**

```csharp
// Changsta.Ai.Tests.Unit/Domain/TrackTests.cs
using Changsta.Ai.Core.Domain;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Domain
{
    [TestFixture]
    public sealed class TrackTests
    {
        [Test]
        public void Equals_IgnoresCuePoint_WhenArtistAndTitleMatch()
        {
            var withCue = new Track { Artist = "Calibre", Title = "Pillow Dub", CuePointSeconds = 248 };
            var withoutCue = new Track { Artist = "Calibre", Title = "Pillow Dub" };

            Assert.That(withCue, Is.EqualTo(withoutCue));
            Assert.That(withCue.GetHashCode(), Is.EqualTo(withoutCue.GetHashCode()));
        }

        [Test]
        public void Equals_DifferentTitle_AreNotEqual()
        {
            var a = new Track { Artist = "Calibre", Title = "Pillow Dub" };
            var b = new Track { Artist = "Calibre", Title = "Mr Majestic" };

            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void CuePointSeconds_DefaultsToNull()
        {
            var track = new Track { Artist = "A", Title = "B" };

            Assert.That(track.CuePointSeconds, Is.Null);
        }
    }
}
```

- [ ] **Step 1.2: Run to confirm compile failure**

Run: `dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental`
Expected: FAIL — `'Track' does not contain a definition for 'CuePointSeconds'`.

- [ ] **Step 1.3: Add the property**

In `Changsta.Ai.Core/Domain/Track.cs`, add the property immediately after `Title` (no XML doc — SA1600 is off and the file has none):

```csharp
        required public string Title { get; init; }

        public int? CuePointSeconds { get; init; }
```

Leave `Equals`, `Equals(object?)`, and `GetHashCode` exactly as they are.

- [ ] **Step 1.4: Build and run tests**

Run: `dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental`
Run: `dotnet test soundcloud-ai-mix-recommender-api.sln --filter "FullyQualifiedName~TrackTests" --no-build`
Expected: PASS.

- [ ] **Step 1.5: Commit**

```bash
git add Changsta.Ai.Core/Domain/Track.cs Changsta.Ai.Tests.Unit/Domain/TrackTests.cs
git commit -m "feat(domain): add optional Track.CuePointSeconds excluded from equality"
```

---

## Task 2: Tracklist marker tolerates a trailing colon

**Files:**
- Modify: `Changsta.Ai.Core/Parsing/MixDescriptionParser.cs`
- Test: `Changsta.Ai.Tests.Unit/Parsing/MixDescriptionParserTests.cs`

**Interfaces:**
- Produces: `SplitDescription`/`ExtractIntro` now recognise a marker line ending in a single `:` (e.g. `Tracklist:`). No signature change. Consumed by Task 3's end-to-end format tests.

- [ ] **Step 2.1: Write the failing tests**

Append to the existing `MixDescriptionParserTests` class:

```csharp
        [Test]
        public void SplitDescription_WhenMarkerHasTrailingColon_FindsMarker()
        {
            const string description =
                "Intro paragraph.\n" +
                "\n" +
                "Tracklist:\n" +
                "Artist A - Track One\n" +
                "Artist B - Track Two\n";

            (string? intro, string trackSection) = MixDescriptionParser.SplitDescription(description);

            Assert.That(intro, Is.EqualTo("Intro paragraph."));
            Assert.That(trackSection, Is.EqualTo("Artist A - Track One\nArtist B - Track Two"));
        }

        [Test]
        public void ExtractIntro_WhenMarkerHasTrailingColon_ReturnsIntro()
        {
            const string description =
                "Intro text.\n" +
                "\n" +
                "Tracklist:\n" +
                "Artist A - Track One\n";

            string? result = MixDescriptionParser.ExtractIntro(description);

            Assert.That(result, Is.EqualTo("Intro text."));
        }
```

- [ ] **Step 2.2: Run to confirm failure**

Run: `dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental`
Run: `dotnet test soundcloud-ai-mix-recommender-api.sln --filter "FullyQualifiedName~MixDescriptionParserTests" --no-build`
Expected: the two new tests FAIL — `Tracklist:` is not recognised, so intro is null and track section is empty.

- [ ] **Step 2.3: Normalise a trailing colon before matching**

In `Changsta.Ai.Core/Parsing/MixDescriptionParser.cs`, change `FindMarkerIndex` to compare a normalised candidate, and add the helper below it:

```csharp
        private static int FindMarkerIndex(string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string line = NormaliseMarkerCandidate(lines[i]);

                for (int j = 0; j < TracklistMarkers.Length; j++)
                {
                    if (string.Equals(line, TracklistMarkers[j], StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static string NormaliseMarkerCandidate(string line)
        {
            string trimmed = line.Trim();

            // Tolerate a trailing colon, e.g. "Tracklist:" — common in numbered / cue-point
            // tracklists — so it is recognised the same as the bare "Tracklist" marker.
            if (trimmed.EndsWith(':'))
            {
                trimmed = trimmed[..^1].TrimEnd();
            }

            return trimmed;
        }
```

- [ ] **Step 2.4: Build and run tests**

Run: `dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental`
Run: `dotnet test soundcloud-ai-mix-recommender-api.sln --filter "FullyQualifiedName~MixDescriptionParserTests" --no-build`
Expected: PASS (all, including the pre-existing cases).

- [ ] **Step 2.5: Commit**

```bash
git add Changsta.Ai.Core/Parsing/MixDescriptionParser.cs Changsta.Ai.Tests.Unit/Parsing/MixDescriptionParserTests.cs
git commit -m "feat(parsing): recognise tracklist markers with a trailing colon"
```

---

## Task 3: Parse line numbers and cue points in `TracklistExtractor`

**Files:**
- Modify: `Changsta.Ai.Infrastructure.Services.SoundCloud/Parsing/TracklistExtractor.cs`
- Test: `Changsta.Ai.Tests.Unit/Parsing/TracklistExtractorTests.cs`

**Interfaces:**
- Consumes: `Track.CuePointSeconds` (Task 1), colon-tolerant marker (Task 2).
- Produces: `TracklistExtractor.Extract` populates `CuePointSeconds` per track (null when no cue point). Signature unchanged: `IReadOnlyList<Track> Extract(string? description)`.

**Cue-point grammar:** `TIME = \d{1,3}:[0-5]\d(?::[0-5]\d)?` (matches `m:ss`, `mm:ss`, `h:mm:ss`). The `[0-5]\d` on the seconds/minutes groups means malformed values like `4:99` are not treated as cues. Seconds = `mm*60 + ss` (2 parts) or `hh*3600 + mm*60 + ss` (3 parts).

- [ ] **Step 3.1: Write the failing tests**

Append to the existing `TracklistExtractorTests` class (keep the existing tests untouched):

```csharp
        [Test]
        public void Extract_LineNumbersOnly_ParsesTracksWithNullCuePoint()
        {
            const string description =
                "Tracklist:\n" +
                "1. Yo Speed - Bring It Back [Original Mix]\n" +
                "2. E.R.N.E.S.T.O - Caracas [Original Mix]\n" +
                "3. Bombo Rosa - Números\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result[0].Artist, Is.EqualTo("Yo Speed"));
            Assert.That(result[0].Title, Is.EqualTo("Bring It Back [Original Mix]"));
            Assert.That(result[0].CuePointSeconds, Is.Null);
            Assert.That(result[1].Artist, Is.EqualTo("E.R.N.E.S.T.O"));
            Assert.That(result[2].Title, Is.EqualTo("Números"));
        }

        [Test]
        public void Extract_LineNumbersWithBracketedCuePoints_ParsesArtistTitleAndSeconds()
        {
            const string description =
                "Tracklist:\n" +
                "1. [0:00] Yo Speed - Bring It Back [Original Mix]\n" +
                "2. [4:08] E.R.N.E.S.T.O - Caracas [Original Mix]\n" +
                "3. [7:21] Bombo Rosa - Números\n" +
                "4. [10:31] Sekret Chadow - Big Breaks [Original Mix]\n" +
                "5. [15:20] Bowser - Shake It [Original Mix]\n" +
                "6. [19:44] BAKEY - JB Riddim\n" +
                "7. [22:44] Deibeat - Rise Up [Freestylers remix]\n" +
                "8. [26:51] 26Rebel MC - Tribal Bass [Original Foundation Mix]\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(8));
            Assert.That(result[0].CuePointSeconds, Is.EqualTo(0));
            Assert.That(result[0].Title, Is.EqualTo("Bring It Back [Original Mix]"));
            Assert.That(result[1].CuePointSeconds, Is.EqualTo(248));
            Assert.That(result[2].CuePointSeconds, Is.EqualTo(441));
            Assert.That(result[3].CuePointSeconds, Is.EqualTo(631));
            Assert.That(result[4].CuePointSeconds, Is.EqualTo(920));
            Assert.That(result[5].CuePointSeconds, Is.EqualTo(1184));
            Assert.That(result[6].CuePointSeconds, Is.EqualTo(1364));
            Assert.That(result[7].CuePointSeconds, Is.EqualTo(1611));
            Assert.That(result[7].Artist, Is.EqualTo("26Rebel MC"));
            Assert.That(result[7].Title, Is.EqualTo("Tribal Bass [Original Foundation Mix]"));
        }

        [Test]
        public void Extract_BracketedCuePointsNoLineNumbers_ParsesSeconds()
        {
            const string description =
                "Tracklist:\n" +
                "[0:00] Yo Speed - Bring It Back [Original Mix]\n" +
                "[4:08] E.R.N.E.S.T.O - Caracas [Original Mix]\n" +
                "[26:51] 26Rebel MC - Tribal Bass [Original Foundation Mix]\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result[0].CuePointSeconds, Is.EqualTo(0));
            Assert.That(result[1].CuePointSeconds, Is.EqualTo(248));
            Assert.That(result[2].CuePointSeconds, Is.EqualTo(1611));
            Assert.That(result[2].Artist, Is.EqualTo("26Rebel MC"));
        }

        [Test]
        public void Extract_BareCuePointsNoBrackets_ParsesSecondsAndPreservesParenTitles()
        {
            const string description =
                "Tracklist:\n" +
                "00:00 Yo Speed - Bring It Back (Original Mix)\n" +
                "04:08 E.R.N.E.S.T.O - Caracas (Original Mix)\n" +
                "07:21 Bombo Rosa - Números\n" +
                "26:51 26Rebel MC - Tribal Bass (Original Foundation Mix)\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(4));
            Assert.That(result[0].CuePointSeconds, Is.EqualTo(0));
            Assert.That(result[0].Title, Is.EqualTo("Bring It Back (Original Mix)"));
            Assert.That(result[1].CuePointSeconds, Is.EqualTo(248));
            Assert.That(result[2].CuePointSeconds, Is.EqualTo(441));
            Assert.That(result[3].CuePointSeconds, Is.EqualTo(1611));
            Assert.That(result[3].Artist, Is.EqualTo("26Rebel MC"));
            Assert.That(result[3].Title, Is.EqualTo("Tribal Bass (Original Foundation Mix)"));
        }

        [Test]
        public void Extract_ExistingPlainFormat_StillYieldsNullCuePoints()
        {
            const string description =
                "Tracklist\n" +
                "Calibre - Pillow Dub\n" +
                "Break - Last Goodbye (ft. Celestine)\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].CuePointSeconds, Is.Null);
            Assert.That(result[1].CuePointSeconds, Is.Null);
            Assert.That(result[1].Title, Is.EqualTo("Last Goodbye (ft. Celestine)"));
        }

        [Test]
        public void Extract_BracketedHoursMinutesSeconds_ParsesSeconds()
        {
            const string description =
                "Tracklist\n" +
                "[1:02:03] Long Artist - Long Track\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].CuePointSeconds, Is.EqualTo(3723));
            Assert.That(result[0].Artist, Is.EqualTo("Long Artist"));
        }

        [Test]
        public void Extract_BareHoursMinutesSeconds_ParsesSeconds()
        {
            const string description =
                "Tracklist\n" +
                "1:02:03 Long Artist - Long Track\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].CuePointSeconds, Is.EqualTo(3723));
        }

        [Test]
        public void Extract_BareTimestampThatIsActuallyAnArtist_IsNotTreatedAsCuePoint()
        {
            // "20:20 - Vision": stripping a bare "20:20 " leaves "- Vision" (no " - "),
            // so it is parsed as artist "20:20", title "Vision", with no cue point.
            const string description =
                "Tracklist\n" +
                "20:20 - Vision\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Artist, Is.EqualTo("20:20"));
            Assert.That(result[0].Title, Is.EqualTo("Vision"));
            Assert.That(result[0].CuePointSeconds, Is.Null);
        }

        [Test]
        public void Extract_LineNumberThenBareCue_StripsBothAndKeepsDigitArtist()
        {
            const string description =
                "Tracklist\n" +
                "8. 26:51 26Rebel MC - Tribal Bass\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].CuePointSeconds, Is.EqualTo(1611));
            Assert.That(result[0].Artist, Is.EqualTo("26Rebel MC"));
            Assert.That(result[0].Title, Is.EqualTo("Tribal Bass"));
        }

        [Test]
        public void Extract_InvalidSecondsValue_IsNotTreatedAsCuePoint()
        {
            // "4:99" — seconds out of range (not [0-5]\d) — is not a cue point.
            const string description =
                "Tracklist\n" +
                "4:99 Some Artist - Some Title\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].CuePointSeconds, Is.Null);
            Assert.That(result[0].Artist, Is.EqualTo("4:99 Some Artist"));
            Assert.That(result[0].Title, Is.EqualTo("Some Title"));
        }

        [Test]
        public void Extract_BracketedCueWithNoSeparator_DropsLine()
        {
            // A cue point but no " - " separator: the line is not a track and is dropped
            // (the cue is computed then discarded). Locks current behaviour.
            const string description =
                "Tracklist\n" +
                "[4:08] Just A Title With No Artist\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Extract_CrlfLineEndingsWithCuePoints_ParsesSeconds()
        {
            const string description =
                "Tracklist\r\n" +
                "[0:00] Yo Speed - Bring It Back\r\n" +
                "[4:08] E.R.N.E.S.T.O - Caracas\r\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].CuePointSeconds, Is.EqualTo(0));
            Assert.That(result[1].CuePointSeconds, Is.EqualTo(248));
        }

        [Test]
        public void Extract_LineThatIsOnlyANumberOrCue_IsSkipped()
        {
            const string description =
                "Tracklist\n" +
                "3.\n" +
                "[4:08]\n" +
                "Real Artist - Real Title\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Artist, Is.EqualTo("Real Artist"));
            Assert.That(result[0].CuePointSeconds, Is.Null);
        }

        [Test]
        public void Extract_MixedTracklist_SomeTracksHaveCuePoints_OthersDoNot()
        {
            // A single tracklist where only some lines carry a cue point (bracketed and bare),
            // interleaved with lines that have none. Each track is independent.
            const string description =
                "Tracklist:\n" +
                "[0:00] Yo Speed - Bring It Back\n" +
                "E.R.N.E.S.T.O - Caracas\n" +
                "07:21 Bombo Rosa - Números\n" +
                "BAKEY - JB Riddim\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(4));
            Assert.That(result[0].CuePointSeconds, Is.EqualTo(0));
            Assert.That(result[1].CuePointSeconds, Is.Null);
            Assert.That(result[2].CuePointSeconds, Is.EqualTo(441));
            Assert.That(result[3].CuePointSeconds, Is.Null);
        }
```

- [ ] **Step 3.2: Run to confirm failure**

Run: `dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental`
Run: `dotnet test soundcloud-ai-mix-recommender-api.sln --filter "FullyQualifiedName~TracklistExtractorTests" --no-build`
Expected: the new cue-point tests FAIL (cue values null / titles still contain the leading `[m:ss]` or number).

- [ ] **Step 3.3: Replace `TracklistExtractor.cs` with the format-aware parser**

```csharp
// Changsta.Ai.Infrastructure.Services.SoundCloud/Parsing/TracklistExtractor.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Parsing;

namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Parsing
{
    public static class TracklistExtractor
    {
        // These patterns are anchored at ^ with bounded quantifiers (\d{1,3}, no nested
        // repetition), so they cannot catastrophically backtrack — no match timeout or
        // RegexMatchTimeoutException handling is required.

        // Leading "1." / "12." list number, e.g. "3. Artist - Title".
        private static readonly Regex LineNumberPrefix = new(
            @"^\d+\.\s*",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // Bracketed cue point, e.g. "[4:08] " or "[1:02:03] ". Group 1 is the timestamp.
        private static readonly Regex BracketedCuePrefix = new(
            @"^\[\s*(\d{1,3}:[0-5]\d(?::[0-5]\d)?)\s*\]\s*",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // Bare cue point, e.g. "04:08 " or "1:02:03 ". Group 1 is the timestamp.
        private static readonly Regex BareCuePrefix = new(
            @"^(\d{1,3}:[0-5]\d(?::[0-5]\d)?)\s+",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static IReadOnlyList<Track> Extract(string? description)
        {
            (string? _, string trackSection) = MixDescriptionParser.SplitDescription(description);

            if (string.IsNullOrWhiteSpace(trackSection))
            {
                return Array.Empty<Track>();
            }

            string[] lines = trackSection
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            var tracks = new List<Track>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string afterNumber = StripLineNumber(line);
                (string body, int? cuePointSeconds) = StripCuePoint(afterNumber);

                int separatorIndex = body.IndexOf(" - ", StringComparison.Ordinal);

                if (separatorIndex < 0)
                {
                    continue;
                }

                string artist = body.Substring(0, separatorIndex).Trim();
                string title = body.Substring(separatorIndex + 3).Trim();

                tracks.Add(new Track
                {
                    Artist = artist,
                    Title = title,
                    CuePointSeconds = cuePointSeconds,
                });
            }

            return tracks;
        }

        private static string StripLineNumber(string line)
        {
            Match match = LineNumberPrefix.Match(line);
            return match.Success ? line[match.Length..] : line;
        }

        // Removes a leading cue point (bracketed "[m:ss]" or bare "m:ss") and returns its value
        // in whole seconds, or null when there is none. A bare timestamp is only treated as a cue
        // point when the remainder still contains an " - " separator — this protects an artist
        // literally named like a timestamp (e.g. "20:20 - Vision").
        private static (string Body, int? CuePointSeconds) StripCuePoint(string line)
        {
            Match bracketed = BracketedCuePrefix.Match(line);
            if (bracketed.Success)
            {
                return (line[bracketed.Length..], ParseCueSeconds(bracketed.Groups[1].Value));
            }

            Match bare = BareCuePrefix.Match(line);
            if (bare.Success)
            {
                string remainder = line[bare.Length..];
                if (remainder.IndexOf(" - ", StringComparison.Ordinal) >= 0)
                {
                    return (remainder, ParseCueSeconds(bare.Groups[1].Value));
                }
            }

            return (line, null);
        }

        private static int ParseCueSeconds(string token)
        {
            string[] parts = token.Split(':');

            if (parts.Length == 2)
            {
                return (int.Parse(parts[0], CultureInfo.InvariantCulture) * 60)
                    + int.Parse(parts[1], CultureInfo.InvariantCulture);
            }

            return (int.Parse(parts[0], CultureInfo.InvariantCulture) * 3600)
                + (int.Parse(parts[1], CultureInfo.InvariantCulture) * 60)
                + int.Parse(parts[2], CultureInfo.InvariantCulture);
        }
    }
}
```

Member order satisfies SA1201/SA1202/SA1204: static readonly fields first, then the public method, then private helpers.

- [ ] **Step 3.4: Build and run tests**

Run: `dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental`
Run: `dotnet test soundcloud-ai-mix-recommender-api.sln --filter "FullyQualifiedName~TracklistExtractorTests" --no-build`
Expected: PASS (new + all pre-existing cases).

- [ ] **Step 3.5: Commit**

```bash
git add Changsta.Ai.Infrastructure.Services.SoundCloud/Parsing/TracklistExtractor.cs \
        Changsta.Ai.Tests.Unit/Parsing/TracklistExtractorTests.cs
git commit -m "feat(parsing): parse line numbers and cue points into Track.CuePointSeconds"
```

---

## Task 4: Persist `cuePointSeconds` in the blob catalogue converter

**Files:**
- Modify: `Changsta.Ai.Infrastructure.Services.Azure/Catalogue/BlobMixCatalogueRepository.cs`
- Test: `Changsta.Ai.Tests.Unit/Catalogue/TrackListJsonConverterTests.cs` (create)

**Interfaces:**
- Consumes: `Track.CuePointSeconds` (Task 1).
- Produces: `internal static readonly JsonSerializerOptions BlobMixCatalogueRepository.JsonOptions` (visibility widened from `private` for the test seam; the Azure project already has `InternalsVisibleTo("Changsta.Ai.Tests.Unit")`). Converter writes `cuePointSeconds` only when present; reads it when present (absent → null). v1 string format still parses (cue null).

- [ ] **Step 4.1: Write the failing tests**

```csharp
// Changsta.Ai.Tests.Unit/Catalogue/TrackListJsonConverterTests.cs
using System.Collections.Generic;
using System.Text.Json;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.Azure.Catalogue;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Catalogue
{
    [TestFixture]
    public sealed class TrackListJsonConverterTests
    {
        private static readonly JsonSerializerOptions Options = BlobMixCatalogueRepository.JsonOptions;

        [Test]
        public void Write_EmitsCuePointSeconds_WhenPresent()
        {
            IReadOnlyList<Track> tracks = new[]
            {
                new Track { Artist = "E.R.N.E.S.T.O", Title = "Caracas", CuePointSeconds = 248 },
            };

            string json = JsonSerializer.Serialize(tracks, Options);

            Assert.That(json, Does.Contain("\"cuePointSeconds\":248"));
        }

        [Test]
        public void Write_OmitsCuePointSeconds_WhenNull()
        {
            IReadOnlyList<Track> tracks = new[]
            {
                new Track { Artist = "Yo Speed", Title = "Bring It Back" },
            };

            string json = JsonSerializer.Serialize(tracks, Options);

            Assert.That(json, Does.Not.Contain("cuePointSeconds"));
        }

        [Test]
        public void RoundTrip_PreservesCuePointSeconds()
        {
            IReadOnlyList<Track> original = new[]
            {
                new Track { Artist = "A", Title = "B", CuePointSeconds = 631 },
                new Track { Artist = "C", Title = "D" },
            };

            string json = JsonSerializer.Serialize(original, Options);
            IReadOnlyList<Track> restored = JsonSerializer.Deserialize<IReadOnlyList<Track>>(json, Options)!;

            Assert.That(restored, Has.Count.EqualTo(2));
            Assert.That(restored[0].CuePointSeconds, Is.EqualTo(631));
            Assert.That(restored[1].CuePointSeconds, Is.Null);
        }

        [Test]
        public void Read_ObjectWithoutCuePoint_YieldsNull()
        {
            const string json = "[{\"artist\":\"A\",\"title\":\"B\"}]";

            IReadOnlyList<Track> tracks = JsonSerializer.Deserialize<IReadOnlyList<Track>>(json, Options)!;

            Assert.That(tracks, Has.Count.EqualTo(1));
            Assert.That(tracks[0].CuePointSeconds, Is.Null);
        }

        [Test]
        public void Read_LegacyV1StringFormat_StillParses_WithNullCuePoint()
        {
            const string json = "[\"Artist - Title\"]";

            IReadOnlyList<Track> tracks = JsonSerializer.Deserialize<IReadOnlyList<Track>>(json, Options)!;

            Assert.That(tracks, Has.Count.EqualTo(1));
            Assert.That(tracks[0].Artist, Is.EqualTo("Artist"));
            Assert.That(tracks[0].Title, Is.EqualTo("Title"));
            Assert.That(tracks[0].CuePointSeconds, Is.Null);
        }
    }
}
```

- [ ] **Step 4.2: Run to confirm failure**

Run: `dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental`
Expected: FAIL — `JsonOptions` is inaccessible (currently `private`).

- [ ] **Step 4.3: Widen `JsonOptions` visibility**

In `BlobMixCatalogueRepository.cs`, change the field declaration from `private` to `internal`:

```csharp
        internal static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new TrackListJsonConverter() },
        };
```

- [ ] **Step 4.4: Write `cuePointSeconds` (only when present)**

In the converter's `Write` method, add the cue point inside the per-track object, after `title`:

```csharp
                for (int i = 0; i < value.Count; i++)
                {
                    writer.WriteStartObject();
                    writer.WriteString("artist", value[i].Artist);
                    writer.WriteString("title", value[i].Title);

                    if (value[i].CuePointSeconds.HasValue)
                    {
                        writer.WriteNumber("cuePointSeconds", value[i].CuePointSeconds.Value);
                    }

                    writer.WriteEndObject();
                }
```

- [ ] **Step 4.5: Read `cuePointSeconds` in the v2 object branch**

In the converter's `Read` method, v2 (`StartObject`) branch, add a local and a property handler, and pass it to the `Track`:

```csharp
                        // v2 format: array of { "artist": "...", "title": "...", "cuePointSeconds": 248? }
                        string artist = string.Empty;
                        string title = string.Empty;
                        int? cuePointSeconds = null;

                        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                        {
                            if (reader.TokenType != JsonTokenType.PropertyName)
                            {
                                continue;
                            }

                            string propName = reader.GetString() ?? string.Empty;
                            reader.Read();

                            if (string.Equals(propName, "artist", StringComparison.OrdinalIgnoreCase))
                            {
                                artist = reader.GetString() ?? string.Empty;
                            }
                            else if (string.Equals(propName, "title", StringComparison.OrdinalIgnoreCase))
                            {
                                title = reader.GetString() ?? string.Empty;
                            }
                            else if (string.Equals(propName, "cuePointSeconds", StringComparison.OrdinalIgnoreCase))
                            {
                                if (reader.TokenType == JsonTokenType.Number)
                                {
                                    cuePointSeconds = reader.GetInt32();
                                }
                            }
                        }

                        tracks.Add(new Track { Artist = artist, Title = title, CuePointSeconds = cuePointSeconds });
```

Leave the v1 (`String`) branch unchanged — legacy string tracks never carry a cue point.

- [ ] **Step 4.6: Build and run tests**

Run: `dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental`
Run: `dotnet test soundcloud-ai-mix-recommender-api.sln --filter "FullyQualifiedName~TrackListJsonConverterTests" --no-build`
Expected: PASS.

- [ ] **Step 4.7: Commit**

```bash
git add Changsta.Ai.Infrastructure.Services.Azure/Catalogue/BlobMixCatalogueRepository.cs \
        Changsta.Ai.Tests.Unit/Catalogue/TrackListJsonConverterTests.cs
git commit -m "feat(catalogue): persist cuePointSeconds in the blob track converter"
```

---

## Task 5: Lock the additive API wire shape

The three per-mix endpoints (`GET /api/catalog/mixes`, `/mixes/{slug}`, `/artists/{name}/mixes`) serialize the domain `Mix` (and its `Tracklist`) with the API's reflection-based camelCase `System.Text.Json` (configured in `Program.cs`). Because Task 1 added `CuePointSeconds` to `Track`, these responses already include `cuePointSeconds` — **no production code change is needed**. This task adds a guard test that locks the field name, casing, and additive (always-present) shape so a future change can't silently drop or rename it.

**Files:**
- Test: `Changsta.Ai.Tests.Unit/Api/TrackApiSerializationTests.cs` (create)

- [ ] **Step 5.1: Write the contract tests**

```csharp
// Changsta.Ai.Tests.Unit/Api/TrackApiSerializationTests.cs
using System.Text.Json;
using Changsta.Ai.Core.Domain;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Api
{
    [TestFixture]
    public sealed class TrackApiSerializationTests
    {
        // Mirrors the API serialization configured in Changsta.Ai.Interface.Api/Program.cs
        // (camelCase, reflection-based — the domain Track has no custom API converter).
        private static readonly JsonSerializerOptions ApiOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        [Test]
        public void Track_SerialisesCuePointSeconds_InCamelCase_WhenPresent()
        {
            var track = new Track { Artist = "E.R.N.E.S.T.O", Title = "Caracas", CuePointSeconds = 248 };

            string json = JsonSerializer.Serialize(track, ApiOptions);

            Assert.That(json, Does.Contain("\"cuePointSeconds\":248"));
        }

        [Test]
        public void Track_EmitsNullCuePointSeconds_WhenAbsent_SoTheShapeIsStable()
        {
            var track = new Track { Artist = "Yo Speed", Title = "Bring It Back" };

            string json = JsonSerializer.Serialize(track, ApiOptions);

            Assert.That(json, Does.Contain("\"cuePointSeconds\":null"));
        }
    }
}
```

- [ ] **Step 5.2: Build and run tests**

Run: `dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental`
Run: `dotnet test soundcloud-ai-mix-recommender-api.sln --filter "FullyQualifiedName~TrackApiSerializationTests" --no-build`
Expected: PASS (these pass against Task 1's property — they exist to prevent regressions).

- [ ] **Step 5.3: Commit**

```bash
git add Changsta.Ai.Tests.Unit/Api/TrackApiSerializationTests.cs
git commit -m "test(api): lock additive cuePointSeconds wire shape on tracklist responses"
```

---

## Task 6: Full-suite verification

This change touches shared parsing, contracts, and serialization, so the full suite must pass (per project conventions).

- [ ] **Step 6.1: Clean build**

Run: `dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental`
Expected: 0 warnings, 0 errors (analyzers are strict; fix any SA1201/SA1202/SA1204 ordering rather than suppress).

- [ ] **Step 6.2: Full test run**

Run: `dotnet test soundcloud-ai-mix-recommender-api.sln --no-build`
Expected: all tests pass, including pre-existing `CatalogProjectionsTests`, `MixCatalogControllerTests`, `RelatedMixScorerTests` (which confirm artist+title dedup/scoring is unaffected by the new field).

- [ ] **Step 6.3: Final note**

No CHANGELOG/release actions here — releasing is a separate, explicitly-requested workflow. After merge, the integration test is the user adding one real mix with cue points and re-ingesting the catalogue.

---

## Self-review

- **Spec coverage:** line-numbers (Task 3), line-numbers+cue (Task 3), cue-only bracketed (Task 3), bare cue (Task 3), trailing-colon marker (Task 2), JSON cache persistence (Task 4), additive API surfacing on every per-mix tracklist response (Tasks 1+5), comprehensive tests (every task). ✓
- **Placeholder scan:** none — every step has concrete code/commands. ✓
- **Type consistency:** `CuePointSeconds` (`int?`) and the `cuePointSeconds` JSON name are used identically across Tasks 1, 3, 4, 5. `StripLineNumber`/`StripCuePoint`/`ParseCueSeconds` are defined and called within Task 3 only. `BlobMixCatalogueRepository.JsonOptions` is widened in Task 4 and consumed by its test in the same task. ✓
- **Aggregates untouched:** `CatalogProjections` deliberately unchanged (decision 4); Task 6 re-runs its tests to prove no regression. ✓
