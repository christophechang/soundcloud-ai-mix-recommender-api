# Related Mixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Embed a ranked `relatedMixes` array (up to 6 refs) on every `Mix` in the catalog, computed at cache-flush time using a deterministic scoring algorithm.

**Architecture:** Add a slim `RelatedMixRef` domain type and a `RelatedMixes` property to `Mix`. A new `RelatedMixScorer` (internal to the Azure infrastructure project) scores each pair using shared tracks, shared artists, genre, energy, moods, and BPM overlap. `BlobBackedMixCatalogueProvider` calls the scorer after merge + intro hydration, before writing back to blob, so the result is pre-computed, persisted, and served from the 24 h memory cache at zero read cost.

**Tech Stack:** .NET 10, NUnit 4, FluentAssertions 8, existing project layout

---

## File Map

| Action | Path | Responsibility |
|---|---|---|
| Create | `Changsta.Ai.Core/Domain/RelatedMixRef.cs` | Slim ref type: Title, Url, ArtworkUrl |
| Modify | `Changsta.Ai.Core/Domain/Mix.cs` | Add `RelatedMixes` property |
| Create | `Changsta.Ai.Infrastructure.Services.Azure/Catalogue/RelatedMixScorer.cs` | Pure scoring algorithm + `WithRelatedMixes` copy helper |
| Modify | `Changsta.Ai.Infrastructure.Services.Azure/Catalogue/BlobBackedMixCatalogueProvider.cs` | Wire scorer; update `WithGenre`, `WithIntro`, `MergeCatalogs` to copy `RelatedMixes`; add `relatedMixesChanged` to write condition |
| Create | `Changsta.Ai.Tests.Unit/Catalogue/RelatedMixScorerTests.cs` | Unit tests for scoring algorithm |
| Modify | `Changsta.Ai.Tests.Unit/Catalogue/BlobBackedMixCatalogueProviderTests.cs` | Tests for related mixes computed + persisted through provider |

---

### Task 1: `RelatedMixRef` domain type and `Mix.RelatedMixes` property

**Files:**
- Create: `Changsta.Ai.Core/Domain/RelatedMixRef.cs`
- Modify: `Changsta.Ai.Core/Domain/Mix.cs`

- [ ] **Step 1: Create `RelatedMixRef`**

```csharp
// Changsta.Ai.Core/Domain/RelatedMixRef.cs
using System;

namespace Changsta.Ai.Core.Domain
{
    public sealed class RelatedMixRef
    {
        required public string Title { get; init; }

        required public string Url { get; init; }

        public string? ArtworkUrl { get; init; }
    }
}
```

- [ ] **Step 2: Add `RelatedMixes` property to `Mix`**

Add after the `Moods` property in `Changsta.Ai.Core/Domain/Mix.cs`:

```csharp
        public IReadOnlyList<RelatedMixRef> RelatedMixes { get; init; } = Array.Empty<RelatedMixRef>();
```

The full file after change:

```csharp
using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.Domain
{
    public sealed class Mix
    {
        required public string Id { get; init; }

        required public string Title { get; init; }

        required public string Url { get; init; }

        public string? Description { get; init; }

        public string? Intro { get; init; }

        public string? Duration { get; init; }

        public string? ImageUrl { get; init; }

        public IReadOnlyList<Track> Tracklist { get; init; } = Array.Empty<Track>();

        required public string Genre { get; init; }

        required public string Energy { get; init; }

        public int? BpmMin { get; init; }

        public int? BpmMax { get; init; }

        public IReadOnlyList<string> Moods { get; init; } = Array.Empty<string>();

        public IReadOnlyList<RelatedMixRef> RelatedMixes { get; init; } = Array.Empty<RelatedMixRef>();

        public DateTimeOffset? PublishedAt { get; init; }
    }
}
```

- [ ] **Step 3: Build to verify no errors**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Changsta.Ai.Core/Domain/RelatedMixRef.cs Changsta.Ai.Core/Domain/Mix.cs
git commit -m "feat: add RelatedMixRef type and RelatedMixes property to Mix"
```

---

### Task 2: Update copy helpers in `BlobBackedMixCatalogueProvider` to carry `RelatedMixes`

**Files:**
- Modify: `Changsta.Ai.Infrastructure.Services.Azure/Catalogue/BlobBackedMixCatalogueProvider.cs`

Three places create new `Mix` objects and must copy `RelatedMixes`:
1. `WithGenre` — called during genre normalization
2. `WithIntro` — called during intro hydration
3. `MergeCatalogs` — creates a new Mix on URL collision (carry from existing blob mix)

New RSS discoveries in `MergeCatalogs` (`result.Add(mix)`) already default to `Array.Empty<RelatedMixRef>()`, which is correct — the scorer will populate them.

- [ ] **Step 1: Update `WithGenre` to copy `RelatedMixes`**

Replace the `WithGenre` method body:

```csharp
        private static Mix WithGenre(Mix mix, string genre)
        {
            return new Mix
            {
                Id = mix.Id,
                Title = mix.Title,
                Url = mix.Url,
                Description = mix.Description,
                Intro = mix.Intro,
                Duration = mix.Duration,
                ImageUrl = mix.ImageUrl,
                Tracklist = mix.Tracklist,
                Genre = genre,
                Energy = mix.Energy,
                BpmMin = mix.BpmMin,
                BpmMax = mix.BpmMax,
                Moods = mix.Moods,
                RelatedMixes = mix.RelatedMixes,
                PublishedAt = mix.PublishedAt,
            };
        }
```

- [ ] **Step 2: Update `WithIntro` to copy `RelatedMixes`**

Replace the `WithIntro` method body:

```csharp
        private static Mix WithIntro(Mix mix, string? intro)
        {
            return new Mix
            {
                Id = mix.Id,
                Title = mix.Title,
                Url = mix.Url,
                Description = mix.Description,
                Intro = intro,
                Duration = mix.Duration,
                ImageUrl = mix.ImageUrl,
                Tracklist = mix.Tracklist,
                Genre = mix.Genre,
                Energy = mix.Energy,
                BpmMin = mix.BpmMin,
                BpmMax = mix.BpmMax,
                Moods = mix.Moods,
                RelatedMixes = mix.RelatedMixes,
                PublishedAt = mix.PublishedAt,
            };
        }
```

- [ ] **Step 3: Update `MergeCatalogs` URL-collision branch to copy `RelatedMixes` from existing blob mix**

In `MergeCatalogs`, the URL-collision `new Mix { ... }` block currently ends at `PublishedAt`. Add `RelatedMixes = existing.RelatedMixes,` before `PublishedAt`:

```csharp
                    byUrl[mix.Url] = new Mix
                    {
                        Id = existing.Id,
                        Title = mix.Title,
                        Url = existing.Url,
                        Description = mix.Description,
                        Intro = mix.Intro,
                        Duration = mix.Duration ?? existing.Duration,
                        ImageUrl = mix.ImageUrl ?? existing.ImageUrl,
                        Tracklist = syncSchema ? mix.Tracklist : existing.Tracklist,
                        Genre = syncSchema ? mix.Genre : existing.Genre,
                        Energy = syncSchema ? mix.Energy : existing.Energy,
                        BpmMin = syncSchema ? mix.BpmMin : existing.BpmMin,
                        BpmMax = syncSchema ? mix.BpmMax : existing.BpmMax,
                        Moods = syncSchema ? mix.Moods : existing.Moods,
                        RelatedMixes = existing.RelatedMixes,
                        PublishedAt = mix.PublishedAt ?? existing.PublishedAt,
                    };
```

- [ ] **Step 4: Build to verify**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Run existing tests to verify nothing broken**

```
dotnet test soundcloud-ai-mix-recommender-api.sln --no-build
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add Changsta.Ai.Infrastructure.Services.Azure/Catalogue/BlobBackedMixCatalogueProvider.cs
git commit -m "fix: copy RelatedMixes in Mix copy helpers and MergeCatalogs"
```

---

### Task 3: Implement `RelatedMixScorer` with TDD

**Files:**
- Create: `Changsta.Ai.Tests.Unit/Catalogue/RelatedMixScorerTests.cs`
- Create: `Changsta.Ai.Infrastructure.Services.Azure/Catalogue/RelatedMixScorer.cs`

Scoring weights:
| Signal | Weight | Cap |
|---|---|---|
| Shared exact track (artist + title) | +15 | 5 tracks |
| Shared tracklist artist | +8 | 5 artists |
| Same genre | +6 | — |
| Same energy | +3 | — |
| Shared mood | +2 | 3 moods |
| BPM range overlap | +1 | — |

Results ordered by score descending, ties by `PublishedAt` descending. Max 6 returned. Mixes with score 0 excluded.

- [ ] **Step 1: Write the failing tests**

Create `Changsta.Ai.Tests.Unit/Catalogue/RelatedMixScorerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.Azure.Catalogue;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Catalogue
{
    [TestFixture]
    public sealed class RelatedMixScorerTests
    {
        [Test]
        public void ComputeRelatedMixes_mix_not_related_to_itself()
        {
            var mix = MakeMix("1", "https://sc.test/mix-1", genre: "dnb");
            var mixes = new[] { mix };

            var result = RelatedMixScorer.ComputeRelatedMixes(mixes, out _);

            result[0].RelatedMixes.Should().BeEmpty();
        }

        [Test]
        public void ComputeRelatedMixes_shared_exact_track_scores_highest()
        {
            var sharedTrack = new Track { Artist = "Skeptical", Title = "Sequence" };

            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb",
                tracklist: new[] { sharedTrack });

            var withSharedTrack = MakeMix("2", "https://sc.test/mix-2", genre: "house",
                tracklist: new[] { sharedTrack });

            var sameGenreOnly = MakeMix("3", "https://sc.test/mix-3", genre: "dnb");

            var mixes = new[] { target, withSharedTrack, sameGenreOnly };

            var result = RelatedMixScorer.ComputeRelatedMixes(mixes, out _);

            result[0].RelatedMixes[0].Url.Should().Be("https://sc.test/mix-2");
        }

        [Test]
        public void ComputeRelatedMixes_shared_artist_scores_above_genre_only()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb",
                tracklist: new[] { new Track { Artist = "Skeptical", Title = "Sequence" } });

            var sharedArtist = MakeMix("2", "https://sc.test/mix-2", genre: "house",
                tracklist: new[] { new Track { Artist = "Skeptical", Title = "Different Track" } });

            var sameGenreOnly = MakeMix("3", "https://sc.test/mix-3", genre: "dnb");

            var mixes = new[] { target, sharedArtist, sameGenreOnly };

            var result = RelatedMixScorer.ComputeRelatedMixes(mixes, out _);

            result[0].RelatedMixes[0].Url.Should().Be("https://sc.test/mix-2");
            result[0].RelatedMixes[1].Url.Should().Be("https://sc.test/mix-3");
        }

        [Test]
        public void ComputeRelatedMixes_same_genre_included_in_results()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb");
            var sameGenre = MakeMix("2", "https://sc.test/mix-2", genre: "dnb");
            var differentGenre = MakeMix("3", "https://sc.test/mix-3", genre: "house");

            var mixes = new[] { target, sameGenre, differentGenre };

            var result = RelatedMixScorer.ComputeRelatedMixes(mixes, out _);

            result[0].RelatedMixes.Should().ContainSingle()
                .Which.Url.Should().Be("https://sc.test/mix-2");
        }

        [Test]
        public void ComputeRelatedMixes_zero_score_mix_excluded()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb", energy: "peak");
            var noMatch = MakeMix("2", "https://sc.test/mix-2", genre: "house", energy: "deep");

            var mixes = new[] { target, noMatch };

            var result = RelatedMixScorer.ComputeRelatedMixes(mixes, out _);

            result[0].RelatedMixes.Should().BeEmpty();
        }

        [Test]
        public void ComputeRelatedMixes_max_six_returned()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb");

            var candidates = new Mix[8];
            for (int i = 0; i < 8; i++)
            {
                candidates[i] = MakeMix(
                    (i + 2).ToString(),
                    $"https://sc.test/mix-{i + 2}",
                    genre: "dnb");
            }

            var allMixes = new List<Mix> { target };
            allMixes.AddRange(candidates);

            var result = RelatedMixScorer.ComputeRelatedMixes(allMixes, out _);

            result[0].RelatedMixes.Should().HaveCount(6);
        }

        [Test]
        public void ComputeRelatedMixes_ties_broken_by_published_at_descending()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb");

            var older = MakeMix("2", "https://sc.test/older", genre: "dnb",
                publishedAt: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

            var newer = MakeMix("3", "https://sc.test/newer", genre: "dnb",
                publishedAt: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

            var mixes = new[] { target, older, newer };

            var result = RelatedMixScorer.ComputeRelatedMixes(mixes, out _);

            result[0].RelatedMixes[0].Url.Should().Be("https://sc.test/newer");
        }

        [Test]
        public void ComputeRelatedMixes_same_energy_adds_score()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb", energy: "peak");

            var sameGenreAndEnergy = MakeMix("2", "https://sc.test/mix-2", genre: "dnb", energy: "peak");
            var sameGenreOnlyDiffEnergy = MakeMix("3", "https://sc.test/mix-3", genre: "dnb", energy: "deep");

            var mixes = new[] { target, sameGenreAndEnergy, sameGenreOnlyDiffEnergy };

            var result = RelatedMixScorer.ComputeRelatedMixes(mixes, out _);

            result[0].RelatedMixes[0].Url.Should().Be("https://sc.test/mix-2");
        }

        [Test]
        public void ComputeRelatedMixes_shared_moods_add_score()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb",
                moods: new[] { "dark", "tense" });

            var sharedMoodsAndGenre = MakeMix("2", "https://sc.test/mix-2", genre: "dnb",
                moods: new[] { "dark", "tense" });

            var genreOnlyNoMoods = MakeMix("3", "https://sc.test/mix-3", genre: "dnb");

            var mixes = new[] { target, sharedMoodsAndGenre, genreOnlyNoMoods };

            var result = RelatedMixScorer.ComputeRelatedMixes(mixes, out _);

            result[0].RelatedMixes[0].Url.Should().Be("https://sc.test/mix-2");
        }

        [Test]
        public void ComputeRelatedMixes_bpm_overlap_adds_score()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb",
                bpmMin: 170, bpmMax: 175);

            var bpmOverlap = MakeMix("2", "https://sc.test/mix-2", genre: "dnb",
                bpmMin: 173, bpmMax: 178);

            var bpmNoOverlap = MakeMix("3", "https://sc.test/mix-3", genre: "dnb",
                bpmMin: 120, bpmMax: 125);

            var mixes = new[] { target, bpmOverlap, bpmNoOverlap };

            var result = RelatedMixScorer.ComputeRelatedMixes(mixes, out _);

            result[0].RelatedMixes[0].Url.Should().Be("https://sc.test/mix-2");
        }

        [Test]
        public void ComputeRelatedMixes_ref_carries_title_url_artworkUrl()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb");
            var related = MakeMix("2", "https://sc.test/mix-2", genre: "dnb",
                title: "Deep Rollers", artworkUrl: "https://i1.sndcdn.com/artwork.jpg");

            var mixes = new[] { target, related };

            var result = RelatedMixScorer.ComputeRelatedMixes(mixes, out _);

            var ref0 = result[0].RelatedMixes[0];
            ref0.Title.Should().Be("Deep Rollers");
            ref0.Url.Should().Be("https://sc.test/mix-2");
            ref0.ArtworkUrl.Should().Be("https://i1.sndcdn.com/artwork.jpg");
        }

        [Test]
        public void ComputeRelatedMixes_changed_false_when_related_mixes_identical()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb");
            var candidate = MakeMix("2", "https://sc.test/mix-2", genre: "dnb");

            var mixes = new[] { target, candidate };

            // First call to compute
            var firstPass = RelatedMixScorer.ComputeRelatedMixes(mixes, out bool firstChanged);
            firstChanged.Should().BeTrue();

            // Second call with pre-computed related mixes — nothing changes
            var secondPass = RelatedMixScorer.ComputeRelatedMixes(firstPass, out bool secondChanged);
            secondChanged.Should().BeFalse();
        }

        [Test]
        public void ComputeRelatedMixes_changed_true_when_related_mixes_empty_on_input()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb");
            var candidate = MakeMix("2", "https://sc.test/mix-2", genre: "dnb");

            var mixes = new[] { target, candidate };

            RelatedMixScorer.ComputeRelatedMixes(mixes, out bool changed);

            changed.Should().BeTrue();
        }

        private static Mix MakeMix(
            string id,
            string url,
            string title = "Test Mix",
            string genre = "dnb",
            string energy = "peak",
            string? artworkUrl = null,
            IReadOnlyList<Track>? tracklist = null,
            IReadOnlyList<string>? moods = null,
            int? bpmMin = null,
            int? bpmMax = null,
            DateTimeOffset? publishedAt = null)
        {
            return new Mix
            {
                Id = id,
                Title = title,
                Url = url,
                Genre = genre,
                Energy = energy,
                ImageUrl = artworkUrl,
                Tracklist = tracklist ?? Array.Empty<Track>(),
                Moods = moods ?? Array.Empty<string>(),
                BpmMin = bpmMin,
                BpmMax = bpmMax,
                PublishedAt = publishedAt,
            };
        }
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail (scorer does not exist yet)**

```
dotnet test soundcloud-ai-mix-recommender-api.sln --no-build --filter "FullyQualifiedName~RelatedMixScorerTests"
```

Expected: Build error or all tests fail — `RelatedMixScorer` not found.

- [ ] **Step 3: Implement `RelatedMixScorer`**

Create `Changsta.Ai.Infrastructure.Services.Azure/Catalogue/RelatedMixScorer.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    internal static class RelatedMixScorer
    {
        private const int MaxRelated = 6;
        private const int MaxSharedTracksCap = 5;
        private const int MaxSharedArtistsCap = 5;
        private const int MaxSharedMoodsCap = 3;
        private const int ScoreSharedTrack = 15;
        private const int ScoreSharedArtist = 8;
        private const int ScoreSameGenre = 6;
        private const int ScoreSameEnergy = 3;
        private const int ScoreSharedMood = 2;
        private const int ScoreBpmOverlap = 1;

        public static IReadOnlyList<Mix> ComputeRelatedMixes(IReadOnlyList<Mix> mixes, out bool changed)
        {
            changed = false;
            var result = new Mix[mixes.Count];

            for (int i = 0; i < mixes.Count; i++)
            {
                Mix mix = mixes[i];
                RelatedMixRef[] related = ScoreRelated(mix, mixes);

                if (!RelatedEquals(mix.RelatedMixes, related))
                {
                    changed = true;
                    result[i] = WithRelatedMixes(mix, related);
                }
                else
                {
                    result[i] = mix;
                }
            }

            return result;
        }

        private static RelatedMixRef[] ScoreRelated(Mix target, IReadOnlyList<Mix> all)
        {
            var trackSet = BuildTrackSet(target.Tracklist);
            var artistSet = BuildArtistSet(target.Tracklist);
            var moodSet = BuildMoodSet(target.Moods);

            return all
                .Where(m => !string.Equals(m.Url, target.Url, StringComparison.OrdinalIgnoreCase))
                .Select(m => (Mix: m, Score: Score(target, m, trackSet, artistSet, moodSet)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Mix.PublishedAt ?? DateTimeOffset.MinValue)
                .Take(MaxRelated)
                .Select(x => new RelatedMixRef
                {
                    Title = x.Mix.Title,
                    Url = x.Mix.Url,
                    ArtworkUrl = x.Mix.ImageUrl,
                })
                .ToArray();
        }

        private static int Score(
            Mix target,
            Mix candidate,
            HashSet<(string Artist, string Title)> targetTracks,
            HashSet<string> targetArtists,
            HashSet<string> targetMoods)
        {
            int score = 0;

            int sharedTracks = candidate.Tracklist
                .Count(t => targetTracks.Contains(
                    (t.Artist.ToLowerInvariant(), t.Title.ToLowerInvariant())));
            score += Math.Min(sharedTracks, MaxSharedTracksCap) * ScoreSharedTrack;

            int sharedArtists = candidate.Tracklist
                .Select(t => t.Artist.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .Count(a => targetArtists.Contains(a));
            score += Math.Min(sharedArtists, MaxSharedArtistsCap) * ScoreSharedArtist;

            if (!string.IsNullOrEmpty(target.Genre)
                && string.Equals(target.Genre, candidate.Genre, StringComparison.OrdinalIgnoreCase))
            {
                score += ScoreSameGenre;
            }

            if (!string.IsNullOrEmpty(target.Energy)
                && string.Equals(target.Energy, candidate.Energy, StringComparison.OrdinalIgnoreCase))
            {
                score += ScoreSameEnergy;
            }

            int sharedMoods = candidate.Moods
                .Count(m => targetMoods.Contains(m.ToLowerInvariant()));
            score += Math.Min(sharedMoods, MaxSharedMoodsCap) * ScoreSharedMood;

            if (BpmOverlap(target, candidate))
            {
                score += ScoreBpmOverlap;
            }

            return score;
        }

        private static bool BpmOverlap(Mix a, Mix b)
        {
            if (a.BpmMin is null || a.BpmMax is null || b.BpmMin is null || b.BpmMax is null)
            {
                return false;
            }

            return a.BpmMin <= b.BpmMax && b.BpmMin <= a.BpmMax;
        }

        private static HashSet<(string, string)> BuildTrackSet(IReadOnlyList<Track> tracks)
        {
            var set = new HashSet<(string, string)>();
            foreach (Track t in tracks)
            {
                set.Add((t.Artist.ToLowerInvariant(), t.Title.ToLowerInvariant()));
            }

            return set;
        }

        private static HashSet<string> BuildArtistSet(IReadOnlyList<Track> tracks)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (Track t in tracks)
            {
                set.Add(t.Artist.ToLowerInvariant());
            }

            return set;
        }

        private static HashSet<string> BuildMoodSet(IReadOnlyList<string> moods)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (string m in moods)
            {
                set.Add(m.ToLowerInvariant());
            }

            return set;
        }

        private static bool RelatedEquals(IReadOnlyList<RelatedMixRef> existing, RelatedMixRef[] computed)
        {
            if (existing.Count != computed.Length)
            {
                return false;
            }

            for (int i = 0; i < existing.Count; i++)
            {
                if (!string.Equals(existing[i].Url, computed[i].Url, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static Mix WithRelatedMixes(Mix mix, IReadOnlyList<RelatedMixRef> related)
        {
            return new Mix
            {
                Id = mix.Id,
                Title = mix.Title,
                Url = mix.Url,
                Description = mix.Description,
                Intro = mix.Intro,
                Duration = mix.Duration,
                ImageUrl = mix.ImageUrl,
                Tracklist = mix.Tracklist,
                Genre = mix.Genre,
                Energy = mix.Energy,
                BpmMin = mix.BpmMin,
                BpmMax = mix.BpmMax,
                Moods = mix.Moods,
                RelatedMixes = related,
                PublishedAt = mix.PublishedAt,
            };
        }
    }
}
```

- [ ] **Step 4: Build**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Run scorer tests**

```
dotnet test soundcloud-ai-mix-recommender-api.sln --no-build --filter "FullyQualifiedName~RelatedMixScorerTests"
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add Changsta.Ai.Infrastructure.Services.Azure/Catalogue/RelatedMixScorer.cs \
        Changsta.Ai.Tests.Unit/Catalogue/RelatedMixScorerTests.cs
git commit -m "feat: add RelatedMixScorer with deterministic track/artist/genre/energy/mood/bpm scoring"
```

---

### Task 4: Wire `RelatedMixScorer` into `BlobBackedMixCatalogueProvider`

**Files:**
- Modify: `Changsta.Ai.Infrastructure.Services.Azure/Catalogue/BlobBackedMixCatalogueProvider.cs`
- Modify: `Changsta.Ai.Tests.Unit/Catalogue/BlobBackedMixCatalogueProviderTests.cs`

- [ ] **Step 1: Write failing tests first**

Add these three tests to `BlobBackedMixCatalogueProviderTests` (append before the closing brace of the class, after existing tests):

```csharp
        [Test]
        public async Task GetLatestAsync_related_mixes_computed_and_written_to_blob_on_first_run()
        {
            var mix1 = MakeMix("1", "https://sc.test/mix-1", genre: "dnb");
            var mix2 = MakeMix("2", "https://sc.test/mix-2", genre: "dnb");

            var blobRepo = new StubBlobRepository();

            var sut = BuildSut(
                blobRepo: blobRepo,
                blobMixes: Array.Empty<Mix>(),
                rssMixes: new[] { mix1, mix2 });

            var result = await sut.GetLatestAsync(10, CancellationToken.None);

            Assert.That(result[0].RelatedMixes, Has.Count.EqualTo(1));
            Assert.That(result[0].RelatedMixes[0].Url, Is.EqualTo("https://sc.test/mix-2"));
            Assert.That(blobRepo.WrittenMixes![0].RelatedMixes, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task GetLatestAsync_related_mixes_stable_no_extra_write()
        {
            // Pre-populate blob with already-computed related mixes so no change is detected.
            var ref2 = new RelatedMixRef { Title = "Mix 2", Url = "https://sc.test/mix-2", ArtworkUrl = null };
            var ref1 = new RelatedMixRef { Title = "Mix 1", Url = "https://sc.test/mix-1", ArtworkUrl = null };

            var mix1 = new Mix
            {
                Id = "1", Title = "Mix 1", Url = "https://sc.test/mix-1",
                Genre = "dnb", Energy = "peak",
                RelatedMixes = new[] { ref2 },
            };
            var mix2 = new Mix
            {
                Id = "2", Title = "Mix 2", Url = "https://sc.test/mix-2",
                Genre = "dnb", Energy = "peak",
                RelatedMixes = new[] { ref1 },
            };

            var blobRepo = new StubBlobRepository { BlobMixes = new[] { mix1, mix2 } };

            var sut = BuildSut(
                blobRepo: blobRepo,
                blobMixes: new[] { mix1, mix2 },
                rssMixes: Array.Empty<Mix>());

            await sut.GetLatestAsync(10, CancellationToken.None);

            Assert.That(blobRepo.WriteCallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task GetLatestAsync_related_mixes_recomputed_when_new_mix_added()
        {
            var existingRef = new RelatedMixRef
            {
                Title = "Mix 2", Url = "https://sc.test/mix-2", ArtworkUrl = null,
            };

            var blobMix1 = new Mix
            {
                Id = "1", Title = "Mix 1", Url = "https://sc.test/mix-1",
                Genre = "dnb", Energy = "peak",
                RelatedMixes = new[] { existingRef },
            };
            var blobMix2 = new Mix
            {
                Id = "2", Title = "Mix 2", Url = "https://sc.test/mix-2",
                Genre = "dnb", Energy = "peak",
                RelatedMixes = Array.Empty<RelatedMixRef>(),
            };

            var newRssMix = MakeMix("3", "https://sc.test/mix-3", genre: "dnb");

            var blobRepo = new StubBlobRepository { BlobMixes = new[] { blobMix1, blobMix2 } };

            var sut = BuildSut(
                blobRepo: blobRepo,
                blobMixes: new[] { blobMix1, blobMix2 },
                rssMixes: new[] { newRssMix });

            var result = await sut.GetLatestAsync(10, CancellationToken.None);

            // New mix should appear in related mixes for existing mixes
            Assert.That(result.Any(m => m.RelatedMixes.Any(r => r.Url == "https://sc.test/mix-3")),
                Is.True);
            Assert.That(blobRepo.WriteCallCount, Is.EqualTo(1));
        }
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test soundcloud-ai-mix-recommender-api.sln --no-build --filter "FullyQualifiedName~BlobBackedMixCatalogueProviderTests"
```

Expected: 3 new tests fail.

- [ ] **Step 3: Wire scorer into `GetLatestAsync` in `BlobBackedMixCatalogueProvider`**

After the `HydrateIntros` call and before the write condition block, add:

```csharp
                bool relatedMixesChanged;
                merged = RelatedMixScorer.ComputeRelatedMixes(merged, out relatedMixesChanged);
```

Update the write condition to include `relatedMixesChanged`:

```csharp
                if (blobReadSucceeded && (newDiscoveries > 0 || updatedEntries > 0 || blobGenresChanged || introHydrationChanged || relatedMixesChanged))
```

The full updated block in `GetLatestAsync` (lines 86–108 region) becomes:

```csharp
                IReadOnlyList<Mix> merged = MergeCatalogs(blobMixes, rssMixes);

                bool introHydrationChanged;
                merged = HydrateIntros(merged, out introHydrationChanged);

                bool relatedMixesChanged;
                merged = RelatedMixScorer.ComputeRelatedMixes(merged, out relatedMixesChanged);

                int newDiscoveries = CountNewDiscoveries(blobMixes, rssMixes);
                int updatedEntries = CountUpdatedEntries(blobMixes, rssMixes);

                if (blobReadSucceeded && (newDiscoveries > 0 || updatedEntries > 0 || blobGenresChanged || introHydrationChanged || relatedMixesChanged))
                {
                    _logger.LogInformation(
                        "Writing blob catalog — {NewCount} new mixes, {UpdateCount} updated entries, genreNormalizationChanged={GenreNormalizationChanged}.",
                        newDiscoveries,
                        updatedEntries,
                        blobGenresChanged);

                    await _repository.WriteAsync(merged, cancellationToken).ConfigureAwait(false);
                }
```

- [ ] **Step 4: Build**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Run all tests**

```
dotnet test soundcloud-ai-mix-recommender-api.sln --no-build
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add Changsta.Ai.Infrastructure.Services.Azure/Catalogue/BlobBackedMixCatalogueProvider.cs \
        Changsta.Ai.Tests.Unit/Catalogue/BlobBackedMixCatalogueProviderTests.cs
git commit -m "feat: compute and persist related mixes at cache flush"
```

---

## Self-Review

**Spec coverage:**
- `RelatedMixRef` with title, permalink_url (mapped to `Url`), artwork_url (`ArtworkUrl`) ✓
- Scoring: shared exact track > shared artist > genre > energy > moods > BPM ✓
- Max 6 results per mix ✓
- Ordered by score descending, ties by recency ✓
- Computed at flush time, zero overhead on reads ✓
- Persisted to blob ✓
- `relatedMixesChanged` triggers blob write only when needed ✓
- API response carries `relatedMixes` automatically (Mix is serialized directly) ✓

**Placeholder scan:** No TBDs, no "similar to above", all code blocks complete.

**Type consistency:** `RelatedMixRef.Url` used throughout. `RelatedMixScorer.ComputeRelatedMixes` signature matches across all tasks.
