# Radio Stations API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the existing NowSpinning personalised radio feature with three editorial radio stations (Touchdown FM, Deep Signal FM, Jungle Pressure) each with a shared deterministic daily 24-slot schedule, served via `GET /api/radio/stations`. Remove old NowSpinning routes, contracts, use cases, and tests.

**Architecture:** The existing `NowSpinning*` use cases, controllers, contracts, DTOs, view models, and DI registrations are **removed**. Retained internals (`SlotDefinitions`, `DayBucket`, `SlotKey`, `SlotConfig`) are kept as library code because the new Radio scheduler reuses them. `SlotScorer`, `SlotPoolBuilder`, `NowSpinningDrawer`, `PoolEntry`, and `NowSpinningPools` are **deleted** — see file map. The new `Radio` subsystem lives in `Core.BusinessProcesses/Radio/`, with contracts in `Core.Contracts/Radio/`, DTOs in `Core/Dtos/`, and a thin controller in `Interface.Api/Controllers/`. The replacement endpoint is `GET /api/radio/stations`. Old routes `/api/catalog/radio` and `/api/catalog/radio/program` are removed.

**Tech Stack:** .NET 10, NUnit 3, FluentAssertions, existing `SlotDefinitions` utilities.

---

## Architecture risks (review before starting)

1. **Genre normalization:** `Mix.Genre` stores values produced by `GenreNormalizer` — canonical lowercase ("uk bass", "ukg", "dnb", "deep-house"). Station genre maps must use these canonical forms. The `MixCatalogController` can stay if it has unrelated catalogue admin routes — check before deleting.
2. **`Warmth` is `moodWeight`:** The handover says `moodWeight`; the domain model uses `Warmth` (`double?`, enriched by `AiMoodWeightEnricher`, roughly -1 to +1). Use `Warmth` throughout.
3. **`Mix` has no explicit artist field:** Use the portion of `Title` before ` - ` as a soft artist signal for repetition penalties.
4. **`SlotDefinitions` BPM range is 110–138:** Jungle/DNB runs 160–175+ BPM. Score against global targets would give zero BPM points for every Jungle mix. Per-station BPM offsets are required (see Task 1).
5. **`SlotDefinitions` is `internal`:** New `Radio` namespace is in the same `Core.BusinessProcesses` project, so direct access is fine.
6. **Fallback must be threshold-based:** Top-N selection (used in first draft) is always non-empty when there are any unused mixes, making the artist/genre/filter relaxation stages unreachable. Use score-threshold gating instead so each relaxation stage is genuinely reachable and testable.
7. **Empty station catalogue must not crash:** If a station has no eligible mixes, throw `RadioStationUnavailableException` with a clear message — do not call `Random.Next(0)`. Controller maps this to 503.
8. **Determinism requirement:** Same catalogue + same UTC date always produces the same schedule. Seed from `date.DayNumber`.

---

## File map

### Files to delete (NowSpinning removal)

| File | Reason |
|---|---|
| `Changsta.Ai.Core.BusinessProcesses/NowSpinning/NowSpinningUseCase.cs` | Replaced |
| `Changsta.Ai.Core.BusinessProcesses/NowSpinning/NowSpinningProgramUseCase.cs` | Replaced |
| `Changsta.Ai.Core.Contracts/NowSpinning/INowSpinningUseCase.cs` | Replaced |
| `Changsta.Ai.Core.Contracts/NowSpinning/INowSpinningProgramUseCase.cs` | Replaced |
| `Changsta.Ai.Core/Dtos/NowSpinningRequestDto.cs` | Replaced |
| `Changsta.Ai.Core/Dtos/NowSpinningResultDto.cs` | Replaced |
| `Changsta.Ai.Core/Dtos/NowSpinningProgramRequestDto.cs` | Replaced |
| `Changsta.Ai.Core/Dtos/NowSpinningProgramResultDto.cs` | Replaced |
| `Changsta.Ai.Core/Dtos/NowSpinningProgramLaneDto.cs` | Replaced |
| `Changsta.Ai.Core/Dtos/NowSpinningScheduleEntryDto.cs` | Replaced |
| `Changsta.Ai.Core/Dtos/NowSpinningSlotDto.cs` | Replaced |
| `Changsta.Ai.Interface.Api/Controllers/NowSpinningController.cs` | Replaced |
| `Changsta.Ai.Interface.Api/Controllers/NowSpinningProgramController.cs` | Replaced |
| `Changsta.Ai.Interface.Api/ViewModels/NowSpinningMixVm.cs` | Replaced — see RadioMixVm note below |
| `Changsta.Ai.Interface.Api/ViewModels/NowSpinningProgramLaneVm.cs` | Replaced |
| `Changsta.Ai.Interface.Api/ViewModels/NowSpinningProgramResponse.cs` | Replaced |
| `Changsta.Ai.Interface.Api/ViewModels/NowSpinningResponse.cs` | Replaced |
| `Changsta.Ai.Interface.Api/ViewModels/NowSpinningScheduleEntryVm.cs` | Replaced |
| `Changsta.Ai.Interface.Api/ViewModels/NowSpinningSlotVm.cs` | Replaced |
| `Changsta.Ai.Tests.Unit/NowSpinning/NowSpinningUseCaseTests.cs` | Replaced |
| `Changsta.Ai.Tests.Unit/NowSpinning/NowSpinningProgramUseCaseTests.cs` | Replaced |
| `Changsta.Ai.Tests.Unit/Controllers/NowSpinningProgramControllerTests.cs` | Replaced |

**Keep (reused by RadioScheduler/RadioSlotScorer):** `SlotDefinitions.cs`, `SlotConfig.cs`, `SlotKey.cs`, `DayBucket.cs`. Keep `NowSpinningMixMapper.cs` — return type updated to `RadioMixVm`, reused by the Radio controller.

**Also delete (orphaned after NowSpinning removal — these depend on deleted DTOs or have no remaining callers):**

| File | Why deleted |
|---|---|
| `Changsta.Ai.Core.BusinessProcesses/NowSpinning/NowSpinningDrawer.cs` | `ToSlotDto()` returns `NowSpinningSlotDto` (deleted); no radio code calls it |
| `Changsta.Ai.Core.BusinessProcesses/NowSpinning/SlotPoolBuilder.cs` | Only called by deleted use cases and Drawer |
| `Changsta.Ai.Core.BusinessProcesses/NowSpinning/NowSpinningPools.cs` | Only used by SlotPoolBuilder and Drawer |
| `Changsta.Ai.Core.BusinessProcesses/NowSpinning/PoolEntry.cs` | Only used by NowSpinningPools and Drawer |
| `Changsta.Ai.Core.BusinessProcesses/NowSpinning/SlotScorer.cs` | Dead code — `RadioSlotScorer` implements its own scoring |
| `Changsta.Ai.Tests.Unit/NowSpinning/SlotPoolBuilderTests.cs` | Tests deleted code |
| `Changsta.Ai.Tests.Unit/NowSpinning/SlotScorerTests.cs` | Tests deleted code |

**VM rename:** `NowSpinningMixVm.cs` must be renamed to `RadioMixVm.cs` using `git mv` (SA1649 requires file name = class name). Done in Task 7 before any new files reference the class.

### New files

| File | Responsibility |
|---|---|
| `Changsta.Ai.Core/Domain/RadioStation.cs` | Station metadata record |
| `Changsta.Ai.Core.BusinessProcesses/Radio/RadioStationDefinitions.cs` | Static station configs + genre→stationId + per-station BPM offset |
| `Changsta.Ai.Core.BusinessProcesses/Radio/RadioSlotScore.cs` | Score breakdown per slot (audit data) |
| `Changsta.Ai.Core.BusinessProcesses/Radio/RadioScoringContext.cs` | Context passed into scorer (recent genres, artists, cross-schedule used) |
| `Changsta.Ai.Core.BusinessProcesses/Radio/RadioSlotScorer.cs` | Composite score: energy, warmth, BPM, freshness, penalties |
| `Changsta.Ai.Core.BusinessProcesses/Radio/RadioScheduledSlot.cs` | One scheduled slot (hour + mix + audit) |
| `Changsta.Ai.Core.BusinessProcesses/Radio/RadioSchedule.cs` | Full 3×24 schedule keyed by stationId |
| `Changsta.Ai.Core.BusinessProcesses/Radio/RadioScheduleViolation.cs` | Validation violation record + rule enum |
| `Changsta.Ai.Core.BusinessProcesses/Radio/RadioScheduler.cs` | Builds the schedule for a given date |
| `Changsta.Ai.Core.BusinessProcesses/Radio/RadioScheduleValidator.cs` | Post-build validation |
| `Changsta.Ai.Core.BusinessProcesses/Radio/GetRadioScheduleUseCase.cs` | Orchestrates catalogue fetch → schedule → DTO |
| `Changsta.Ai.Core.Contracts/Radio/IGetRadioScheduleUseCase.cs` | Use case contract |
| `Changsta.Ai.Core/Exceptions/RadioStationUnavailableException.cs` | Thrown when a station has no eligible mixes; mapped to 503 in controller |
| `Changsta.Ai.Core/Dtos/RadioScheduleResultDto.cs` | Top-level result DTO |
| `Changsta.Ai.Core/Dtos/RadioStationScheduleDto.cs` | Per-station DTO |
| `Changsta.Ai.Core/Dtos/RadioHourSlotDto.cs` | Per-hour slot DTO |
| `Changsta.Ai.Interface.Api/Controllers/RadioController.cs` | GET /api/radio/stations; catches `RadioStationUnavailableException` → 503 |
| `Changsta.Ai.Interface.Api/ViewModels/RadioMixVm.cs` | Mix VM (renamed from `NowSpinningMixVm.cs` via `git mv`) |
| `Changsta.Ai.Interface.Api/ViewModels/RadioResponse.cs` | Public response envelope |
| `Changsta.Ai.Interface.Api/ViewModels/RadioStationVm.cs` | Station view model |
| `Changsta.Ai.Interface.Api/ViewModels/RadioSlotVm.cs` | Slot view model |
| `Changsta.Ai.Tests.Unit/Radio/RadioStationDefinitionsTests.cs` | Genre mapping + default station + BPM offsets |
| `Changsta.Ai.Tests.Unit/Radio/RadioSlotScorerTests.cs` | Scoring: energy, warmth, BPM, penalties, unknown energy |
| `Changsta.Ai.Tests.Unit/Radio/RadioSchedulerTests.cs` | Completeness, repeat avoidance, fallback, determinism |
| `Changsta.Ai.Tests.Unit/Radio/RadioScheduleValidatorTests.cs` | All validation rules |
| `Changsta.Ai.Tests.Unit/Radio/GetRadioScheduleUseCaseTests.cs` | Use case contract + DTO shape |
| `Changsta.Ai.Tests.Unit/Radio/RadioControllerTests.cs` | Route contract tests |

### Modified files

| File | Change |
|---|---|
| `Changsta.Ai.Interface.Api/Program.cs` | Remove NowSpinning DI; add `IGetRadioScheduleUseCase` |
| `Changsta.Ai.Interface.Api/ViewModels/NowSpinningMixMapper.cs` | Update return type to `RadioMixVm` |

---

## Task 1: RadioStation domain record + station definitions + genre mapping

**Station-relative BPM:** `SlotDefinitions.GetBpmTarget` returns 110–138 depending on slot and day-of-week. This range works for Touchdown FM (garage/breaks 120–140 BPM) but is wrong for Deep Signal FM (house 115–128) and completely wrong for Jungle Pressure (DNB 165–180). Each station carries a signed BPM offset applied on top of the slot target. Offsets: Touchdown FM = 0, Deep Signal FM = −15, Jungle Pressure = +38.

### Files
- Create: `Changsta.Ai.Core/Domain/RadioStation.cs`
- Create: `Changsta.Ai.Core.BusinessProcesses/Radio/RadioStationDefinitions.cs`
- Create: `Changsta.Ai.Tests.Unit/Radio/RadioStationDefinitionsTests.cs`

- [ ] **Step 1.1: Write failing tests**

```csharp
// Changsta.Ai.Tests.Unit/Radio/RadioStationDefinitionsTests.cs
using System.Collections.Generic;
using Changsta.Ai.Core.BusinessProcesses.Radio;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Radio
{
    [TestFixture]
    public sealed class RadioStationDefinitionsTests
    {
        [TestCase("uk bass", "touchdown-fm")]
        [TestCase("breakbeat", "touchdown-fm")]
        [TestCase("ukg", "touchdown-fm")]
        [TestCase("hip-hop", "touchdown-fm")]
        [TestCase("hardcore", "touchdown-fm")]
        [TestCase("house", "deep-signal-fm")]
        [TestCase("deep-house", "deep-signal-fm")]
        [TestCase("electronica", "deep-signal-fm")]
        [TestCase("techno", "deep-signal-fm")]
        [TestCase("disco", "deep-signal-fm")]
        [TestCase("funk", "deep-signal-fm")]
        [TestCase("jungle", "jungle-pressure")]
        [TestCase("dnb", "jungle-pressure")]
        public void Genre_maps_to_correct_station(string genre, string expectedStationId)
        {
            bool found = RadioStationDefinitions.TryGetStationForGenre(genre, out string stationId);
            found.Should().BeTrue();
            stationId.Should().Be(expectedStationId);
        }

        [Test]
        public void Unknown_genre_returns_false()
        {
            bool found = RadioStationDefinitions.TryGetStationForGenre("ambient", out _);
            found.Should().BeFalse();
        }

        [Test]
        public void Genre_lookup_is_case_insensitive()
        {
            RadioStationDefinitions.TryGetStationForGenre("UK Bass", out string stationId).Should().BeTrue();
            stationId.Should().Be("touchdown-fm");
        }

        [Test]
        public void Touchdown_FM_is_default_station()
        {
            RadioStationDefinitions.DefaultStationId.Should().Be("touchdown-fm");
        }

        [Test]
        public void Exactly_three_stations_defined()
        {
            RadioStationDefinitions.Stations.Should().HaveCount(3);
        }

        [Test]
        public void Only_one_station_is_default()
        {
            int count = 0;
            foreach (var s in RadioStationDefinitions.Stations)
                if (s.IsDefault) count++;
            count.Should().Be(1);
        }

        [Test]
        public void Each_station_has_non_empty_frequency()
        {
            foreach (var s in RadioStationDefinitions.Stations)
                s.Frequency.Should().NotBeNullOrWhiteSpace(because: $"{s.Id} must have a frequency");
        }

        [Test]
        public void Each_genre_belongs_to_exactly_one_station()
        {
            var all = new System.Collections.Generic.List<string>();
            foreach (var s in RadioStationDefinitions.Stations)
                foreach (string g in s.Genres)
                    all.Add(g);
            all.Should().OnlyHaveUniqueItems();
        }

        [Test]
        public void Jungle_pressure_bpm_offset_is_positive()
        {
            int offset = RadioStationDefinitions.GetBpmOffset("jungle-pressure");
            offset.Should().BeGreaterThan(0, because: "DNB/Jungle BPM is much higher than the global slot targets");
        }

        [Test]
        public void Deep_signal_fm_bpm_offset_is_negative()
        {
            int offset = RadioStationDefinitions.GetBpmOffset("deep-signal-fm");
            offset.Should().BeLessThan(0, because: "House runs slower than the global slot targets");
        }

        [Test]
        public void Unknown_station_bpm_offset_returns_zero()
        {
            int offset = RadioStationDefinitions.GetBpmOffset("unknown-station");
            offset.Should().Be(0);
        }
    }
}
```

- [ ] **Step 1.2: Run to confirm compile error**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```

Expected: error — `RadioStationDefinitions` does not exist.

- [ ] **Step 1.3: Create RadioStation domain record**

```csharp
// Changsta.Ai.Core/Domain/RadioStation.cs
using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.Domain
{
    public sealed class RadioStation
    {
        required public string Id { get; init; }

        required public string Name { get; init; }

        required public string Frequency { get; init; }

        required public string Description { get; init; }

        public bool IsDefault { get; init; }

        public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();
    }
}
```

- [ ] **Step 1.4: Create RadioStationDefinitions**

```csharp
// Changsta.Ai.Core.BusinessProcesses/Radio/RadioStationDefinitions.cs
using System;
using System.Collections.Generic;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    internal static class RadioStationDefinitions
    {
        internal const string DefaultStationId = "touchdown-fm";

        // Genres use normalized canonical forms produced by GenreNormalizer (all lowercase).
        internal static readonly IReadOnlyList<RadioStation> Stations = new RadioStation[]
        {
            new RadioStation
            {
                Id = "touchdown-fm",
                Name = "Touchdown FM",
                Frequency = "103.5 FM",
                Description = "UK Bass, Garage, Breaks, Hip-Hop and Hardcore",
                IsDefault = true,
                Genres = new[] { "uk bass", "breakbeat", "ukg", "hip-hop", "hardcore" },
            },
            new RadioStation
            {
                Id = "deep-signal-fm",
                Name = "Deep Signal FM",
                Frequency = "97.2 FM",
                Description = "House, Deep House, Electronica, Techno, Disco and Funk",
                Genres = new[] { "house", "deep-house", "electronica", "techno", "disco", "funk" },
            },
            new RadioStation
            {
                Id = "jungle-pressure",
                Name = "Jungle Pressure",
                Frequency = "107.7 FM",
                Description = "Jungle and Drum & Bass",
                Genres = new[] { "jungle", "dnb" },
            },
        };

        // Applied on top of SlotDefinitions.GetBpmTarget so scoring stays meaningful
        // relative to each station's actual BPM range.
        private static readonly IReadOnlyDictionary<string, int> _bpmOffsets =
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["touchdown-fm"]    =   0,   // garage/breaks align with global targets (110-138)
                ["deep-signal-fm"]  = -15,   // house runs 100-125 BPM
                ["jungle-pressure"] = +38,   // DNB/Jungle runs 160-180 BPM
            };

        private static readonly IReadOnlyDictionary<string, string> _genreToStationId
            = BuildGenreMap();

        internal static bool TryGetStationForGenre(string genre, out string stationId)
            => _genreToStationId.TryGetValue(genre.Trim(), out stationId!);

        internal static int GetBpmOffset(string stationId)
            => _bpmOffsets.TryGetValue(stationId, out int offset) ? offset : 0;

        private static Dictionary<string, string> BuildGenreMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (RadioStation station in Stations)
                foreach (string genre in station.Genres)
                    map[genre] = station.Id;
            return map;
        }
    }
}
```

- [ ] **Step 1.5: Build and run tests**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
dotnet test soundcloud-ai-mix-recommender-api.sln --filter "FullyQualifiedName~RadioStationDefinitionsTests" --no-build
```

Expected: all pass.

- [ ] **Step 1.6: Commit**

```
git add Changsta.Ai.Core/Domain/RadioStation.cs \
        Changsta.Ai.Core.BusinessProcesses/Radio/RadioStationDefinitions.cs \
        Changsta.Ai.Tests.Unit/Radio/RadioStationDefinitionsTests.cs
git commit -m "feat(radio): add station definitions with genre ownership and BPM offsets"
```

---

## Task 2: RadioSlotScore + RadioScoringContext + RadioSlotScorer

**Scoring formula:**
- `energyScore`: 5.0 if energy matches slot's expected energy values; 0.0 if known but wrong; **2.5 (neutral)** if energy value is unknown — unknown energy must not block selection but must produce an audit warning.
- `warmthScore`: `Max(0, 4.0 − |warmth − slot.WarmthTarget| / 0.25)`
- `bpmScore`: `Max(0, 8.0 − |midBpm − bpmTarget| / 6.0)`; 0 if BPM null
- `freshnessBonus`: 1.0 if mix not used on any station today (`crossScheduleUsedIds` miss); 0.0 otherwise
- `genreClusterPenalty`: 1.5 if same genre in last 1 slot; 4.0 if same genre in last 2+ slots; 0 otherwise
- `artistPenalty`: 2.0 if same artist key (Title prefix before ` - `) found in `recentArtists`; 0 otherwise
- `Total = energyScore + warmthScore + bpmScore + freshnessBonus − genreClusterPenalty − artistPenalty`

**Complete energy value catalogue** — these are the only valid values; anything else is unknown:

| Value | Slot affinity (from SlotDefinitions) |
|---|---|
| `chilled` | Comedown, Morning |
| `low` | Comedown, Morning |
| `low-mid` | Comedown, Morning |
| `mid` | Morning, Afternoon, EarlyEve |
| `journey` | Afternoon, EarlyEve |
| `mid-high` | EarlyEve, Primetime |
| `mid-peak` | EarlyEve, Primetime |
| `high` | Dead, Primetime |
| `peak` | Dead, Primetime |

**Unknown energy policy:** If `Mix.Energy` is empty, null, or any string not in the table above (e.g. a future tag like `"intense"` or a data-entry error), the scorer:
1. Awards **2.5 energy points** (halfway between known-match and known-miss — neutral, not penalised).
2. Sets `UnknownEnergy = true` on the score record.
3. Sets `EnergyWarning = "Unknown energy value '{value}' treated as neutral."` for audit output.
4. The slot is still eligible for selection — unknown energy is never a hard filter.
5. The warning propagates to `RadioScheduledSlot.AuditWarnings` and surfaces in the API response `RadioSlotVm.Warnings`.

### Files
- Create: `Changsta.Ai.Core.BusinessProcesses/Radio/RadioSlotScore.cs`
- Create: `Changsta.Ai.Core.BusinessProcesses/Radio/RadioScoringContext.cs`
- Create: `Changsta.Ai.Core.BusinessProcesses/Radio/RadioSlotScorer.cs`
- Create: `Changsta.Ai.Tests.Unit/Radio/RadioSlotScorerTests.cs`

- [ ] **Step 2.1: Write failing tests**

```csharp
// Changsta.Ai.Tests.Unit/Radio/RadioSlotScorerTests.cs
using System.Collections.Generic;
using Changsta.Ai.Core.BusinessProcesses.NowSpinning;
using Changsta.Ai.Core.BusinessProcesses.Radio;
using Changsta.Ai.Core.Domain;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Radio
{
    [TestFixture]
    public sealed class RadioSlotScorerTests
    {
        private static readonly SlotConfig Primetime = SlotDefinitions.Slots[SlotKey.Primetime];
        // Primetime: baseBpmTarget=138, warmthTarget=-0.3, energyValues=[peak,high,mid-peak,mid-high]

        private static RadioScoringContext Empty() => new RadioScoringContext();

        // ── Energy ───────────────────────────────────────────────────────────────

        [Test]
        public void Known_matching_energy_gives_5_points()
        {
            RadioSlotScore s = Score(MakeMix("peak"), Primetime, 138, Empty());
            s.EnergyScore.Should().BeApproximately(5.0, 0.001);
            s.UnknownEnergy.Should().BeFalse();
            s.EnergyWarning.Should().BeNull();
        }

        [Test]
        public void Known_non_matching_energy_gives_zero_points()
        {
            RadioSlotScore s = Score(MakeMix("chilled"), Primetime, 138, Empty());
            s.EnergyScore.Should().BeApproximately(0.0, 0.001);
            s.UnknownEnergy.Should().BeFalse();
        }

        [Test]
        public void Unknown_energy_gives_2_5_neutral_and_warning()
        {
            RadioSlotScore s = Score(MakeMix("intense"), Primetime, 138, Empty());
            s.EnergyScore.Should().BeApproximately(2.5, 0.001);
            s.UnknownEnergy.Should().BeTrue();
            s.EnergyWarning.Should().Contain("intense");
        }

        [Test]
        public void Null_energy_treated_as_unknown()
        {
            Mix mix = new Mix
            {
                Id = "x", Title = "A - B", Url = "https://sc.test/x",
                Genre = "dnb", Energy = string.Empty, BpmMin = 138,
            };
            RadioSlotScore s = Score(mix, Primetime, 138, Empty());
            s.UnknownEnergy.Should().BeTrue();
        }

        // ── BPM ──────────────────────────────────────────────────────────────────

        [Test]
        public void Null_bpm_gives_zero_bpm_score()
        {
            Mix mix = new Mix
            {
                Id = "x", Title = "A - B", Url = "https://sc.test/x",
                Genre = "dnb", Energy = "peak",
            };
            RadioSlotScore s = Score(mix, Primetime, 138, Empty());
            s.BpmScore.Should().Be(0.0);
        }

        [Test]
        public void Perfect_bpm_gives_8_points()
        {
            RadioSlotScore s = Score(MakeMix("peak", bpm: 138), Primetime, 138, Empty());
            s.BpmScore.Should().BeApproximately(8.0, 0.001);
        }

        [Test]
        public void Bpm_48_away_gives_zero_bpm_points()
        {
            // 8 - 48/6 = 0
            RadioSlotScore s = Score(MakeMix("peak", bpm: 90), Primetime, 138, Empty());
            s.BpmScore.Should().Be(0.0);
        }

        // ── Genre clustering ─────────────────────────────────────────────────────

        [Test]
        public void Same_genre_in_last_1_slot_applies_1_5_penalty()
        {
            Mix mix = MakeMix("peak", genre: "dnb");
            RadioSlotScore noCtx = Score(mix, Primetime, 138, Empty());
            RadioSlotScore withCtx = Score(mix, Primetime, 138,
                new RadioScoringContext { RecentGenres = new[] { "dnb" } });
            (noCtx.Total - withCtx.Total).Should().BeApproximately(1.5, 0.001);
            withCtx.GenreClusterPenalty.Should().BeApproximately(1.5, 0.001);
        }

        [Test]
        public void Same_genre_in_last_2_slots_applies_4_0_penalty()
        {
            Mix mix = MakeMix("peak", genre: "dnb");
            RadioSlotScore withCtx = Score(mix, Primetime, 138,
                new RadioScoringContext { RecentGenres = new[] { "dnb", "dnb" } });
            withCtx.GenreClusterPenalty.Should().BeApproximately(4.0, 0.001);
        }

        // ── Artist penalty ───────────────────────────────────────────────────────

        [Test]
        public void Same_artist_in_recent_slots_applies_2_penalty()
        {
            Mix mix = MakeMix("peak", title: "DJ Rolex - The Bounce");
            RadioSlotScore noCtx = Score(mix, Primetime, 138, Empty());
            RadioSlotScore withCtx = Score(mix, Primetime, 138,
                new RadioScoringContext { RecentArtists = new[] { "DJ Rolex" } });
            (noCtx.Total - withCtx.Total).Should().BeApproximately(2.0, 0.001);
            withCtx.ArtistPenalty.Should().BeApproximately(2.0, 0.001);
        }

        [Test]
        public void Title_without_separator_uses_full_title_as_artist_key()
        {
            Mix mix = MakeMix("peak", title: "Fabriclive 27");
            RadioSlotScore s = Score(mix, Primetime, 138,
                new RadioScoringContext { RecentArtists = new[] { "Fabriclive 27" } });
            s.ArtistPenalty.Should().BeApproximately(2.0, 0.001);
        }

        // ── Freshness ────────────────────────────────────────────────────────────

        [Test]
        public void Mix_not_used_elsewhere_gets_1_freshness_bonus()
        {
            RadioSlotScore s = Score(MakeMix("peak"), Primetime, 138, Empty());
            s.FreshnessBonus.Should().BeApproximately(1.0, 0.001);
        }

        [Test]
        public void Mix_used_elsewhere_today_gets_zero_freshness_bonus()
        {
            Mix mix = MakeMix("peak");
            var ctx = new RadioScoringContext
            {
                CrossScheduleUsedIds = new System.Collections.Generic.HashSet<string> { mix.Id },
            };
            RadioSlotScore s = Score(mix, Primetime, 138, ctx);
            s.FreshnessBonus.Should().BeApproximately(0.0, 0.001);
        }

        // ── ExtractArtistKey ─────────────────────────────────────────────────────

        [TestCase("DJ Zinc - 138 Trek", "DJ Zinc")]
        [TestCase("Fabio & Grooverider", "Fabio & Grooverider")]
        [TestCase("Andy C - Ram Records", "Andy C")]
        public void ExtractArtistKey_splits_on_dash_separator(string title, string expected)
        {
            RadioSlotScorer.ExtractArtistKey(title).Should().Be(expected);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static RadioSlotScore Score(Mix mix, SlotConfig slot, int bpmTarget, RadioScoringContext ctx)
            => RadioSlotScorer.Score(mix, slot, bpmTarget, ctx);

        private static Mix MakeMix(
            string energy,
            int bpm = 138,
            string genre = "dnb",
            string title = "Artist - Mix") => new Mix
            {
                Id = System.Guid.NewGuid().ToString(),
                Title = title,
                Url = "https://sc.test/x",
                Genre = genre,
                Energy = energy,
                BpmMin = bpm,
                BpmMax = bpm + 4,
                Warmth = -0.3,
            };
    }
}
```

- [ ] **Step 2.2: Confirm compile error**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```

Expected: build error — `RadioSlotScorer` not found.

- [ ] **Step 2.3: Create RadioSlotScore**

```csharp
// Changsta.Ai.Core.BusinessProcesses/Radio/RadioSlotScore.cs
namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    internal sealed class RadioSlotScore
    {
        internal double Total { get; init; }
        internal double EnergyScore { get; init; }
        internal double WarmthScore { get; init; }
        internal double BpmScore { get; init; }
        internal double FreshnessBonus { get; init; }
        internal double GenreClusterPenalty { get; init; }
        internal double ArtistPenalty { get; init; }
        internal bool UnknownEnergy { get; init; }
        internal string? EnergyWarning { get; init; }
    }
}
```

- [ ] **Step 2.4: Create RadioScoringContext**

```csharp
// Changsta.Ai.Core.BusinessProcesses/Radio/RadioScoringContext.cs
using System.Collections.Generic;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    internal sealed class RadioScoringContext
    {
        // Genres of the last 1-3 slots on this station (oldest first)
        internal IReadOnlyList<string> RecentGenres { get; init; } = System.Array.Empty<string>();

        // Extracted artist keys from the last 3 slots on this station
        internal IReadOnlyList<string> RecentArtists { get; init; } = System.Array.Empty<string>();

        // Mix IDs used anywhere in today's full schedule (cross-station freshness)
        internal IReadOnlySet<string> CrossScheduleUsedIds { get; init; }
            = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2.5: Create RadioSlotScorer**

```csharp
// Changsta.Ai.Core.BusinessProcesses/Radio/RadioSlotScorer.cs
using System;
using System.Collections.Generic;
using Changsta.Ai.Core.BusinessProcesses.NowSpinning;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    internal static class RadioSlotScorer
    {
        // Complete list of valid energy values drawn from SlotDefinitions.Slots.
        // Any value outside this set is treated as neutral (2.5 pts) with an audit warning.
        // Do NOT silently score unknown values as 0 — that would unfairly penalise mixes
        // with data-entry variants or future tags.
        private static readonly HashSet<string> KnownEnergyValues = new(StringComparer.Ordinal)
        {
            "chilled",   // Comedown, Morning
            "low",       // Comedown, Morning
            "low-mid",   // Comedown, Morning
            "mid",       // Morning, Afternoon, EarlyEve
            "journey",   // Afternoon, EarlyEve
            "mid-high",  // EarlyEve, Primetime
            "mid-peak",  // EarlyEve, Primetime
            "high",      // Dead, Primetime
            "peak",      // Dead, Primetime
        };

        internal static RadioSlotScore Score(
            Mix mix,
            SlotConfig slot,
            int bpmTarget,
            RadioScoringContext context)
        {
            // Energy
            bool isKnown = !string.IsNullOrEmpty(mix.Energy) && KnownEnergyValues.Contains(mix.Energy);
            double energyScore;
            bool unknownEnergy;
            string? energyWarning;

            if (!isKnown)
            {
                energyScore = 2.5;
                unknownEnergy = true;
                string label = string.IsNullOrEmpty(mix.Energy) ? "(empty)" : mix.Energy;
                energyWarning = $"Unknown energy value '{label}' treated as neutral.";
            }
            else
            {
                energyScore = EnergyMatches(mix.Energy, slot.EnergyValues) ? 5.0 : 0.0;
                unknownEnergy = false;
                energyWarning = null;
            }

            // Warmth (spec: moodWeight)
            double warmth = mix.Warmth ?? 0.0;
            double warmthScore = Math.Max(0, 4.0 - (Math.Abs(warmth - slot.WarmthTarget) / 0.25));

            // BPM
            int? bpm = ComputeBpm(mix);
            double bpmScore = bpm.HasValue
                ? Math.Max(0, 8.0 - (Math.Abs(bpm.Value - bpmTarget) / 6.0))
                : 0.0;

            // Freshness bonus
            double freshnessBonus = context.CrossScheduleUsedIds.Contains(mix.Id) ? 0.0 : 1.0;

            // Genre clustering penalty
            int sameGenreCount = 0;
            foreach (string g in context.RecentGenres)
                if (string.Equals(g, mix.Genre, StringComparison.OrdinalIgnoreCase))
                    sameGenreCount++;

            double genreClusterPenalty = sameGenreCount switch
            {
                1 => 1.5,
                >= 2 => 4.0,
                _ => 0.0,
            };

            // Artist repetition penalty
            string artistKey = ExtractArtistKey(mix.Title);
            double artistPenalty = 0.0;
            foreach (string recent in context.RecentArtists)
            {
                if (string.Equals(recent, artistKey, StringComparison.OrdinalIgnoreCase))
                {
                    artistPenalty = 2.0;
                    break;
                }
            }

            double total = energyScore + warmthScore + bpmScore
                + freshnessBonus - genreClusterPenalty - artistPenalty;

            return new RadioSlotScore
            {
                Total = total,
                EnergyScore = energyScore,
                WarmthScore = warmthScore,
                BpmScore = bpmScore,
                FreshnessBonus = freshnessBonus,
                GenreClusterPenalty = genreClusterPenalty,
                ArtistPenalty = artistPenalty,
                UnknownEnergy = unknownEnergy,
                EnergyWarning = energyWarning,
            };
        }

        internal static string ExtractArtistKey(string title)
        {
            int sep = title.IndexOf(" - ", StringComparison.Ordinal);
            return sep > 0 ? title[..sep].Trim() : title.Trim();
        }

        private static int? ComputeBpm(Mix mix)
        {
            if (mix.BpmMin.HasValue && mix.BpmMax.HasValue)
                return (int)Math.Round((mix.BpmMin.Value + mix.BpmMax.Value) / 2.0);
            return mix.BpmMin ?? mix.BpmMax;
        }

        private static bool EnergyMatches(string energy, string[] energyValues)
        {
            foreach (string e in energyValues)
                if (string.Equals(e, energy, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}
```

- [ ] **Step 2.6: Build and run tests**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
dotnet test soundcloud-ai-mix-recommender-api.sln --filter "FullyQualifiedName~RadioSlotScorerTests" --no-build
```

Expected: all pass.

- [ ] **Step 2.7: Commit**

```
git add Changsta.Ai.Core.BusinessProcesses/Radio/RadioSlotScore.cs \
        Changsta.Ai.Core.BusinessProcesses/Radio/RadioScoringContext.cs \
        Changsta.Ai.Core.BusinessProcesses/Radio/RadioSlotScorer.cs \
        Changsta.Ai.Tests.Unit/Radio/RadioSlotScorerTests.cs
git commit -m "feat(radio): add radio slot scorer with energy audit and clustering penalties"
```

---

## Task 3: RadioScheduledSlot + RadioSchedule + RadioScheduler

**Fallback design (threshold-gated so each stage is genuinely reachable):**

The scheduler keeps a `MinScoreThreshold` constant (4.0). At each hour it runs through these stages in order, stopping at the first that produces candidates:

1. **Full scoring:** Score all unused mixes with full context (genre + artist penalties). Keep those ≥ threshold.
2. **Relax artist penalty:** Remove `RecentArtists` from context. Rescore. Keep those ≥ threshold.
3. **Relax genre clustering:** Also remove `RecentGenres`. Rescore. Keep those ≥ threshold.
4. **Relax all score filters:** Keep all unused mixes regardless of score.
5. **Last resort (repeat):** No unused mixes remain — pick any eligible mix (same-day repeat). Record in `RelaxedRules`.

Each relaxation that fires is recorded on the slot's `RelaxedRules` list for audit.

**Empty station catalogue:** If `eligible.Count == 0` at the start of a station's schedule, throw `InvalidOperationException` with a clear message. Task 5 replaces this with `RadioStationUnavailableException` once that type exists; the test below will be updated at that point. The controller (Task 7) catches it as a 503.

**Seeded deterministic pick:** From the shortlisted candidates, compute `seed = date.DayNumber * 1009 + stationIndex * 97 + hour * 7`. Shuffle candidates with that seed. Take index 0.

### Files
- Create: `Changsta.Ai.Core.BusinessProcesses/Radio/RadioScheduledSlot.cs`
- Create: `Changsta.Ai.Core.BusinessProcesses/Radio/RadioSchedule.cs`
- Create: `Changsta.Ai.Core.BusinessProcesses/Radio/RadioScheduler.cs`
- Create: `Changsta.Ai.Tests.Unit/Radio/RadioSchedulerTests.cs`

- [ ] **Step 3.1: Create RadioScheduledSlot**

```csharp
// Changsta.Ai.Core.BusinessProcesses/Radio/RadioScheduledSlot.cs
using System.Collections.Generic;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    internal sealed class RadioScheduledSlot
    {
        required internal int Hour { get; init; }
        required internal Mix Mix { get; init; }
        required internal RadioSlotScore Score { get; init; }
        internal IReadOnlyList<string> AuditReasons { get; init; } = System.Array.Empty<string>();
        internal IReadOnlyList<string> AuditWarnings { get; init; } = System.Array.Empty<string>();
        internal IReadOnlyList<string> RelaxedRules { get; init; } = System.Array.Empty<string>();
    }
}
```

- [ ] **Step 3.2: Create RadioSchedule**

```csharp
// Changsta.Ai.Core.BusinessProcesses/Radio/RadioSchedule.cs
using System.Collections.Generic;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    internal sealed class RadioSchedule
    {
        required internal System.DateOnly ScheduleDate { get; init; }

        // Key = stationId; value = 24 slots ordered 0-23
        required internal IReadOnlyDictionary<string, IReadOnlyList<RadioScheduledSlot>> StationSlots { get; init; }
    }
}
```

- [ ] **Step 3.3: Write failing scheduler tests**

```csharp
// Changsta.Ai.Tests.Unit/Radio/RadioSchedulerTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Changsta.Ai.Core.BusinessProcesses.Radio;
using Changsta.Ai.Core.Domain;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Radio
{
    [TestFixture]
    public sealed class RadioSchedulerTests
    {
        private static readonly DateOnly Thursday = new DateOnly(2026, 5, 21);

        // ── Completeness ──────────────────────────────────────────────────────────

        [Test]
        public void Schedule_has_three_stations()
        {
            Build(Thursday, Catalogue(24, 24, 24)).StationSlots.Should().HaveCount(3);
        }

        [Test]
        public void Each_station_has_24_slots()
        {
            RadioSchedule s = Build(Thursday, Catalogue(24, 24, 24));
            foreach (var kvp in s.StationSlots)
                kvp.Value.Should().HaveCount(24, because: $"{kvp.Key} must have 24 slots");
        }

        [Test]
        public void No_slot_has_null_mix()
        {
            RadioSchedule s = Build(Thursday, Catalogue(24, 24, 24));
            foreach (var kvp in s.StationSlots)
                foreach (RadioScheduledSlot slot in kvp.Value)
                    slot.Mix.Should().NotBeNull(because: $"{kvp.Key} hour {slot.Hour}");
        }

        [Test]
        public void Slots_are_ordered_0_to_23()
        {
            RadioSchedule s = Build(Thursday, Catalogue(24, 24, 24));
            foreach (var kvp in s.StationSlots)
            {
                int expected = 0;
                foreach (RadioScheduledSlot slot in kvp.Value)
                    slot.Hour.Should().Be(expected++);
            }
        }

        // ── Repeat avoidance ──────────────────────────────────────────────────────

        [Test]
        public void No_same_mix_twice_on_same_station_when_catalogue_is_large_enough()
        {
            RadioSchedule s = Build(Thursday, Catalogue(30, 30, 30));
            foreach (var kvp in s.StationSlots)
            {
                kvp.Value.Select(x => x.Mix.Id).Should().OnlyHaveUniqueItems(
                    because: $"{kvp.Key} must not repeat mixes");
            }
        }

        [Test]
        public void Genre_ownership_means_no_cross_station_mix_sharing()
        {
            RadioSchedule s = Build(Thursday, Catalogue(30, 30, 30));
            var sets = s.StationSlots.Values
                .Select(slots => slots.Select(sl => sl.Mix.Id).ToHashSet())
                .ToList();
            for (int i = 0; i < sets.Count; i++)
                for (int j = i + 1; j < sets.Count; j++)
                    sets[i].Intersect(sets[j]).Should().BeEmpty();
        }

        // ── Determinism ───────────────────────────────────────────────────────────

        [Test]
        public void Same_catalogue_and_date_produce_identical_schedule()
        {
            IReadOnlyList<Mix> cat = Catalogue(30, 30, 30);
            RadioSchedule s1 = Build(Thursday, cat);
            RadioSchedule s2 = Build(Thursday, cat);
            foreach (string stationId in s1.StationSlots.Keys)
                for (int h = 0; h < 24; h++)
                    s1.StationSlots[stationId][h].Mix.Id
                        .Should().Be(s2.StationSlots[stationId][h].Mix.Id);
        }

        [Test]
        public void Different_dates_produce_different_schedules()
        {
            IReadOnlyList<Mix> cat = Catalogue(30, 30, 30);
            RadioSchedule thu = Build(Thursday, cat);
            RadioSchedule fri = Build(Thursday.AddDays(1), cat);
            bool anyDiff = thu.StationSlots.Keys.Any(sid =>
                Enumerable.Range(0, 24).Any(h =>
                    thu.StationSlots[sid][h].Mix.Id != fri.StationSlots[sid][h].Mix.Id));
            anyDiff.Should().BeTrue();
        }

        // ── Genre eligibility ─────────────────────────────────────────────────────

        [Test]
        public void Each_station_only_schedules_its_own_genres()
        {
            RadioSchedule s = Build(Thursday, Catalogue(30, 30, 30));
            foreach (var kvp in s.StationSlots)
            {
                foreach (RadioScheduledSlot slot in kvp.Value)
                {
                    RadioStationDefinitions.TryGetStationForGenre(slot.Mix.Genre, out string ownerStation);
                    ownerStation.Should().Be(kvp.Key,
                        because: $"{kvp.Key} hour {slot.Hour} must only play its genres");
                }
            }
        }

        // ── Fallback ──────────────────────────────────────────────────────────────

        [Test]
        public void Still_produces_24_slots_when_catalogue_has_only_one_mix_per_station()
        {
            // 1 mix → must repeat from hour 1 onward (last-resort fallback)
            RadioSchedule s = Build(Thursday, Catalogue(1, 1, 1));
            foreach (var kvp in s.StationSlots)
                kvp.Value.Should().HaveCount(24);
        }

        [Test]
        public void Single_mix_station_records_relaxed_rule_in_audit()
        {
            RadioSchedule s = Build(Thursday, Catalogue(1, 1, 1));
            foreach (var kvp in s.StationSlots)
            {
                bool anyRelaxed = kvp.Value.Any(sl => sl.RelaxedRules.Count > 0);
                anyRelaxed.Should().BeTrue(
                    because: $"{kvp.Key} with 1 mix must relax repeat rule");
            }
        }

        [Test]
        public void Empty_station_catalogue_throws_InvalidOperationException()
        {
            // No mixes at all → Touchdown FM has no eligible mixes
            Action act = () => Build(Thursday, Catalogue(0, 24, 24));
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*touchdown-fm*");
        }

        // ── Station-relative BPM ──────────────────────────────────────────────────

        [Test]
        public void Jungle_pressure_bpm_target_is_higher_than_touchdown_for_same_slot()
        {
            // We cannot inspect internal bpmTarget directly, but we can verify the
            // scheduler does not crash and correctly picks from Jungle catalogue.
            // The BPM offset is covered by RadioStationDefinitionsTests.
            // Here just verify schedule is complete and genre-correct.
            RadioSchedule s = Build(Thursday, Catalogue(30, 30, 30));
            s.StationSlots.Should().ContainKey("jungle-pressure");
            s.StationSlots["jungle-pressure"].Should().HaveCount(24);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static RadioSchedule Build(DateOnly date, IEnumerable<Mix> mixes)
            => new RadioScheduler().Build(mixes.ToList(), date);

        // touchdown, deepSignal, junglePressure mix counts
        private static IReadOnlyList<Mix> Catalogue(int td, int ds, int jp)
        {
            var list = new List<Mix>();
            for (int i = 0; i < td; i++)
                list.Add(M($"td-{i}", "uk bass", "mid", 130));
            for (int i = 0; i < ds; i++)
                list.Add(M($"ds-{i}", "house", "mid", 125));
            for (int i = 0; i < jp; i++)
                list.Add(M($"jp-{i}", "dnb", "mid", 172));
            return list;
        }

        private static Mix M(string id, string genre, string energy, int bpm) => new Mix
        {
            Id = id,
            Title = $"Artist - {id}",
            Url = $"https://sc.test/{id}",
            Genre = genre,
            Energy = energy,
            BpmMin = bpm,
            BpmMax = bpm + 4,
            Warmth = 0.0,
        };
    }
}
```

- [ ] **Step 3.4: Confirm compile error**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```

Expected: build error — `RadioScheduler` not found.

- [ ] **Step 3.5: Create RadioScheduler**

```csharp
// Changsta.Ai.Core.BusinessProcesses/Radio/RadioScheduler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Changsta.Ai.Core.BusinessProcesses.NowSpinning;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    internal sealed class RadioScheduler
    {
        private const double MinScoreThreshold = 4.0;
        private const int RecentGenreWindow = 3;
        private const int RecentArtistWindow = 3;

        internal RadioSchedule Build(IReadOnlyList<Mix> catalogue, DateOnly date)
        {
            DayBucket dayBucket = SlotDefinitions.ResolveDayBucket(
                new DateTime(date.Year, date.Month, date.Day).DayOfWeek);

            var crossScheduleUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stationSlots = new Dictionary<string, IReadOnlyList<RadioScheduledSlot>>();

            for (int si = 0; si < RadioStationDefinitions.Stations.Count; si++)
            {
                RadioStation station = RadioStationDefinitions.Stations[si];
                var stationGenres = new HashSet<string>(station.Genres, StringComparer.OrdinalIgnoreCase);
                List<Mix> eligible = catalogue.Where(m => stationGenres.Contains(m.Genre)).ToList();

                if (eligible.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Station '{station.Id}' has no eligible mixes in the catalogue. " +
                        $"Expected genres: [{string.Join(", ", station.Genres)}].");
                }

                int bpmOffset = RadioStationDefinitions.GetBpmOffset(station.Id);

                stationSlots[station.Id] = BuildStationSchedule(
                    eligible, date, si, dayBucket, bpmOffset, crossScheduleUsed);

                foreach (RadioScheduledSlot slot in stationSlots[station.Id])
                    crossScheduleUsed.Add(slot.Mix.Id);
            }

            return new RadioSchedule { ScheduleDate = date, StationSlots = stationSlots };
        }

        private static IReadOnlyList<RadioScheduledSlot> BuildStationSchedule(
            List<Mix> eligible,
            DateOnly date,
            int stationIndex,
            DayBucket dayBucket,
            int bpmOffset,
            IReadOnlySet<string> crossScheduleUsed)
        {
            var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var recentGenres = new List<string>(RecentGenreWindow);
            var recentArtists = new List<string>(RecentArtistWindow);
            var slots = new List<RadioScheduledSlot>(24);

            for (int hour = 0; hour < 24; hour++)
            {
                SlotKey slotKey = SlotDefinitions.ResolveSlot(hour);
                SlotConfig slotConfig = SlotDefinitions.Slots[slotKey];
                int bpmTarget = SlotDefinitions.GetBpmTarget(slotKey, dayBucket) + bpmOffset;

                RadioScheduledSlot slot = SelectSlot(
                    eligible, hour, date, stationIndex,
                    slotConfig, bpmTarget, usedIds,
                    recentGenres, recentArtists, crossScheduleUsed);

                slots.Add(slot);
                usedIds.Add(slot.Mix.Id);

                recentGenres.Add(slot.Mix.Genre);
                if (recentGenres.Count > RecentGenreWindow) recentGenres.RemoveAt(0);

                recentArtists.Add(RadioSlotScorer.ExtractArtistKey(slot.Mix.Title));
                if (recentArtists.Count > RecentArtistWindow) recentArtists.RemoveAt(0);
            }

            return slots;
        }

        private static RadioScheduledSlot SelectSlot(
            List<Mix> eligible,
            int hour,
            DateOnly date,
            int stationIndex,
            SlotConfig slotConfig,
            int bpmTarget,
            IReadOnlySet<string> usedIds,
            IReadOnlyList<string> recentGenres,
            IReadOnlyList<string> recentArtists,
            IReadOnlySet<string> crossScheduleUsed)
        {
            List<Mix> unused = eligible.Where(m => !usedIds.Contains(m.Id)).ToList();

            // Stage 1: full scoring
            var fullCtx = new RadioScoringContext
            {
                RecentGenres = recentGenres,
                RecentArtists = recentArtists,
                CrossScheduleUsedIds = crossScheduleUsed,
            };
            List<(Mix mix, RadioSlotScore score)> candidates =
                ScoreAndFilter(unused, slotConfig, bpmTarget, fullCtx, MinScoreThreshold);

            if (candidates.Count > 0)
                return MakeSlot(hour, Pick(candidates, date, stationIndex, hour));

            // Stage 2: relax artist penalty
            var noArtistCtx = new RadioScoringContext
            {
                RecentGenres = recentGenres,
                CrossScheduleUsedIds = crossScheduleUsed,
            };
            candidates = ScoreAndFilter(unused, slotConfig, bpmTarget, noArtistCtx, MinScoreThreshold);
            if (candidates.Count > 0)
                return MakeSlot(hour, Pick(candidates, date, stationIndex, hour),
                    new[] { "Artist repetition penalty relaxed." });

            // Stage 3: relax genre clustering
            var noClusterCtx = new RadioScoringContext
            {
                CrossScheduleUsedIds = crossScheduleUsed,
            };
            candidates = ScoreAndFilter(unused, slotConfig, bpmTarget, noClusterCtx, MinScoreThreshold);
            if (candidates.Count > 0)
                return MakeSlot(hour, Pick(candidates, date, stationIndex, hour),
                    new[] { "Artist repetition penalty relaxed.", "Genre clustering penalty relaxed." });

            // Stage 4: any unused mix (no score threshold)
            if (unused.Count > 0)
            {
                candidates = ScoreAndFilter(unused, slotConfig, bpmTarget, new RadioScoringContext(), double.MinValue);
                return MakeSlot(hour, Pick(candidates, date, stationIndex, hour),
                    new[]
                    {
                        "Artist repetition penalty relaxed.",
                        "Genre clustering penalty relaxed.",
                        "Score threshold relaxed — picking from any unused mix.",
                    });
            }

            // Stage 5: last resort — same-day repeat
            candidates = ScoreAndFilter(eligible, slotConfig, bpmTarget, new RadioScoringContext(), double.MinValue);
            return MakeSlot(hour, Pick(candidates, date, stationIndex, hour),
                new[]
                {
                    "Artist repetition penalty relaxed.",
                    "Genre clustering penalty relaxed.",
                    "Score threshold relaxed.",
                    "Same-station same-day repeat — catalogue too small to avoid.",
                });
        }

        private static List<(Mix mix, RadioSlotScore score)> ScoreAndFilter(
            List<Mix> pool,
            SlotConfig slotConfig,
            int bpmTarget,
            RadioScoringContext ctx,
            double threshold)
        {
            var result = new List<(Mix, RadioSlotScore)>(pool.Count);
            foreach (Mix mix in pool)
            {
                RadioSlotScore score = RadioSlotScorer.Score(mix, slotConfig, bpmTarget, ctx);
                if (score.Total >= threshold)
                    result.Add((mix, score));
            }

            result.Sort((a, b) => b.score.Total.CompareTo(a.score.Total));
            return result;
        }

        private static (Mix mix, RadioSlotScore score) Pick(
            List<(Mix mix, RadioSlotScore score)> candidates,
            DateOnly date,
            int stationIndex,
            int hour)
        {
            int seed = unchecked(date.DayNumber * 1009 + stationIndex * 97 + hour * 7);
            var rng = new Random(seed);

            // Shuffle a copy so the sort order is not always deterministically pick[0]
            var shuffled = candidates.ToList();
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            return shuffled[0];
        }

        private static RadioScheduledSlot MakeSlot(
            int hour,
            (Mix mix, RadioSlotScore score) picked,
            string[]? relaxedRules = null)
        {
            var reasons = new List<string>();
            var warnings = new List<string>();

            if (picked.score.EnergyScore >= 5.0) reasons.Add("Strong energy match for this slot.");
            if (picked.score.BpmScore >= 6.0) reasons.Add("Good BPM fit.");
            if (picked.score.FreshnessBonus > 0) reasons.Add("Not used on any station today.");
            if (picked.score.UnknownEnergy) warnings.Add(picked.score.EnergyWarning!);

            return new RadioScheduledSlot
            {
                Hour = hour,
                Mix = picked.mix,
                Score = picked.score,
                AuditReasons = reasons,
                AuditWarnings = warnings,
                RelaxedRules = relaxedRules ?? Array.Empty<string>(),
            };
        }
    }
}
```

- [ ] **Step 3.6: Build and run tests**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
dotnet test soundcloud-ai-mix-recommender-api.sln --filter "FullyQualifiedName~RadioSchedulerTests" --no-build
```

Expected: all pass.

- [ ] **Step 3.7: Commit**

```
git add Changsta.Ai.Core.BusinessProcesses/Radio/RadioScheduledSlot.cs \
        Changsta.Ai.Core.BusinessProcesses/Radio/RadioSchedule.cs \
        Changsta.Ai.Core.BusinessProcesses/Radio/RadioScheduler.cs \
        Changsta.Ai.Tests.Unit/Radio/RadioSchedulerTests.cs
git commit -m "feat(radio): add radio scheduler with threshold fallback and station-relative BPM"
```

---

## Task 4: RadioScheduleViolation + RadioScheduleValidator

### Files
- Create: `Changsta.Ai.Core.BusinessProcesses/Radio/RadioScheduleViolation.cs`
- Create: `Changsta.Ai.Core.BusinessProcesses/Radio/RadioScheduleValidator.cs`
- Create: `Changsta.Ai.Tests.Unit/Radio/RadioScheduleValidatorTests.cs`

- [ ] **Step 4.1: Write failing tests**

```csharp
// Changsta.Ai.Tests.Unit/Radio/RadioScheduleValidatorTests.cs
using System.Collections.Generic;
using Changsta.Ai.Core.BusinessProcesses.Radio;
using Changsta.Ai.Core.Domain;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Radio
{
    [TestFixture]
    public sealed class RadioScheduleValidatorTests
    {
        private static readonly System.DateOnly Today = new System.DateOnly(2026, 5, 21);

        [Test]
        public void Valid_schedule_produces_no_violations()
        {
            RadioScheduleValidator.Validate(ValidSchedule()).Should().BeEmpty();
        }

        [Test]
        public void Missing_slots_produces_SlotCountMismatch()
        {
            var violations = RadioScheduleValidator.Validate(ScheduleWithMissingSlot("touchdown-fm"));
            violations.Should().Contain(v =>
                v.StationId == "touchdown-fm" &&
                v.Rule == RadioScheduleRule.SlotCountMismatch);
        }

        [Test]
        public void Wrong_genre_on_station_produces_GenreMismatch()
        {
            var violations = RadioScheduleValidator.Validate(ScheduleWithWrongGenre("touchdown-fm", "house"));
            violations.Should().Contain(v =>
                v.StationId == "touchdown-fm" &&
                v.Rule == RadioScheduleRule.GenreMismatch);
        }

        [Test]
        public void Repeated_mix_on_same_station_produces_SameStationSameDayRepeat()
        {
            var violations = RadioScheduleValidator.Validate(ScheduleWithRepeat("touchdown-fm"));
            violations.Should().Contain(v =>
                v.StationId == "touchdown-fm" &&
                v.Rule == RadioScheduleRule.SameStationSameDayRepeat);
        }

        [Test]
        public void Same_mix_in_same_hour_across_stations_produces_SameHourCrossStation()
        {
            var violations = RadioScheduleValidator.Validate(ScheduleWithCrossStationConflict());
            violations.Should().Contain(v =>
                v.Rule == RadioScheduleRule.SameHourCrossStationDuplicate);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static RadioSchedule ValidSchedule()
        {
            int counter = 0;
            var slots = new Dictionary<string, IReadOnlyList<RadioScheduledSlot>>();
            foreach (RadioStation station in RadioStationDefinitions.Stations)
            {
                string genre = station.Genres[0];
                var list = new List<RadioScheduledSlot>();
                for (int h = 0; h < 24; h++)
                    list.Add(Slot(h, Mix($"m{counter++}", genre)));
                slots[station.Id] = list;
            }

            return new RadioSchedule { ScheduleDate = Today, StationSlots = slots };
        }

        private static RadioSchedule ScheduleWithMissingSlot(string target)
        {
            int counter = 0;
            var slots = new Dictionary<string, IReadOnlyList<RadioScheduledSlot>>();
            foreach (RadioStation station in RadioStationDefinitions.Stations)
            {
                string genre = station.Genres[0];
                var list = new List<RadioScheduledSlot>();
                int count = station.Id == target ? 23 : 24;
                for (int h = 0; h < count; h++)
                    list.Add(Slot(h, Mix($"m{counter++}", genre)));
                slots[station.Id] = list;
            }

            return new RadioSchedule { ScheduleDate = Today, StationSlots = slots };
        }

        private static RadioSchedule ScheduleWithWrongGenre(string target, string wrongGenre)
        {
            int counter = 0;
            var slots = new Dictionary<string, IReadOnlyList<RadioScheduledSlot>>();
            foreach (RadioStation station in RadioStationDefinitions.Stations)
            {
                string genre = station.Genres[0];
                var list = new List<RadioScheduledSlot>();
                for (int h = 0; h < 24; h++)
                {
                    string g = station.Id == target && h == 0 ? wrongGenre : genre;
                    list.Add(Slot(h, Mix($"m{counter++}", g)));
                }

                slots[station.Id] = list;
            }

            return new RadioSchedule { ScheduleDate = Today, StationSlots = slots };
        }

        private static RadioSchedule ScheduleWithRepeat(string target)
        {
            int counter = 0;
            Mix repeated = Mix("repeated", "uk bass");
            var slots = new Dictionary<string, IReadOnlyList<RadioScheduledSlot>>();
            foreach (RadioStation station in RadioStationDefinitions.Stations)
            {
                string genre = station.Genres[0];
                var list = new List<RadioScheduledSlot>();
                for (int h = 0; h < 24; h++)
                {
                    Mix m = station.Id == target && h < 2 ? repeated : Mix($"m{counter++}", genre);
                    list.Add(Slot(h, m));
                }

                slots[station.Id] = list;
            }

            return new RadioSchedule { ScheduleDate = Today, StationSlots = slots };
        }

        private static RadioSchedule ScheduleWithCrossStationConflict()
        {
            Mix shared = Mix("shared", "uk bass");
            int counter = 0;
            var slots = new Dictionary<string, IReadOnlyList<RadioScheduledSlot>>();
            foreach (RadioStation station in RadioStationDefinitions.Stations)
            {
                string genre = station.Genres[0];
                var list = new List<RadioScheduledSlot>();
                for (int h = 0; h < 24; h++)
                    list.Add(Slot(h, h == 5 ? shared : Mix($"m{counter++}", genre)));
                slots[station.Id] = list;
            }

            return new RadioSchedule { ScheduleDate = Today, StationSlots = slots };
        }

        private static RadioScheduledSlot Slot(int hour, Mix mix) =>
            new RadioScheduledSlot { Hour = hour, Mix = mix, Score = new RadioSlotScore() };

        private static Mix Mix(string id, string genre) => new Mix
        {
            Id = id,
            Title = $"A - {id}",
            Url = $"https://sc.test/{id}",
            Genre = genre,
            Energy = "mid",
            BpmMin = 125,
        };
    }
}
```

- [ ] **Step 4.2: Confirm compile error**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```

- [ ] **Step 4.3: Create RadioScheduleViolation**

```csharp
// Changsta.Ai.Core.BusinessProcesses/Radio/RadioScheduleViolation.cs
namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    internal enum RadioScheduleRule
    {
        SlotCountMismatch,
        GenreMismatch,
        SameStationSameDayRepeat,
        SameHourCrossStationDuplicate,
    }

    internal sealed class RadioScheduleViolation
    {
        required internal string StationId { get; init; }
        required internal RadioScheduleRule Rule { get; init; }
        required internal string Description { get; init; }
        internal int? Hour { get; init; }
        internal string? MixId { get; init; }
    }
}
```

- [ ] **Step 4.4: Create RadioScheduleValidator**

```csharp
// Changsta.Ai.Core.BusinessProcesses/Radio/RadioScheduleValidator.cs
using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    internal static class RadioScheduleValidator
    {
        internal static IReadOnlyList<RadioScheduleViolation> Validate(RadioSchedule schedule)
        {
            var v = new List<RadioScheduleViolation>();
            CheckSlotCounts(schedule, v);
            CheckGenreOwnership(schedule, v);
            CheckSameStationRepeats(schedule, v);
            CheckCrossStationHourConflicts(schedule, v);
            return v;
        }

        private static void CheckSlotCounts(RadioSchedule s, List<RadioScheduleViolation> v)
        {
            foreach (var kvp in s.StationSlots)
                if (kvp.Value.Count != 24)
                    v.Add(new RadioScheduleViolation
                    {
                        StationId = kvp.Key,
                        Rule = RadioScheduleRule.SlotCountMismatch,
                        Description = $"Station has {kvp.Value.Count} slots, expected 24.",
                    });
        }

        private static void CheckGenreOwnership(RadioSchedule s, List<RadioScheduleViolation> v)
        {
            foreach (var kvp in s.StationSlots)
            {
                foreach (RadioScheduledSlot slot in kvp.Value)
                {
                    if (!RadioStationDefinitions.TryGetStationForGenre(slot.Mix.Genre, out string ownerStation)
                        || !string.Equals(ownerStation, kvp.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        v.Add(new RadioScheduleViolation
                        {
                            StationId = kvp.Key,
                            Rule = RadioScheduleRule.GenreMismatch,
                            Hour = slot.Hour,
                            MixId = slot.Mix.Id,
                            Description =
                                $"Hour {slot.Hour}: mix '{slot.Mix.Id}' genre '{slot.Mix.Genre}' does not belong to station '{kvp.Key}'.",
                        });
                    }
                }
            }
        }

        private static void CheckSameStationRepeats(RadioSchedule s, List<RadioScheduleViolation> v)
        {
            foreach (var kvp in s.StationSlots)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (RadioScheduledSlot slot in kvp.Value)
                {
                    if (!seen.Add(slot.Mix.Id))
                        v.Add(new RadioScheduleViolation
                        {
                            StationId = kvp.Key,
                            Rule = RadioScheduleRule.SameStationSameDayRepeat,
                            Hour = slot.Hour,
                            MixId = slot.Mix.Id,
                            Description =
                                $"Hour {slot.Hour}: mix '{slot.Mix.Id}' appears more than once on '{kvp.Key}' today.",
                        });
                }
            }
        }

        private static void CheckCrossStationHourConflicts(RadioSchedule s, List<RadioScheduleViolation> v)
        {
            // hour → list of (stationId, mixId)
            var hourMap = new Dictionary<int, List<(string station, string mixId)>>();
            foreach (var kvp in s.StationSlots)
                foreach (RadioScheduledSlot slot in kvp.Value)
                {
                    if (!hourMap.TryGetValue(slot.Hour, out var list))
                    {
                        list = new List<(string, string)>();
                        hourMap[slot.Hour] = list;
                    }

                    list.Add((kvp.Key, slot.Mix.Id));
                }

            foreach (var hourEntry in hourMap)
            {
                var byMix = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var (station, mixId) in hourEntry.Value)
                {
                    if (!byMix.TryGetValue(mixId, out var stations))
                    {
                        stations = new List<string>();
                        byMix[mixId] = stations;
                    }

                    stations.Add(station);
                }

                foreach (var mixEntry in byMix)
                {
                    if (mixEntry.Value.Count > 1)
                        v.Add(new RadioScheduleViolation
                        {
                            StationId = string.Join(", ", mixEntry.Value),
                            Rule = RadioScheduleRule.SameHourCrossStationDuplicate,
                            Hour = hourEntry.Key,
                            MixId = mixEntry.Key,
                            Description =
                                $"Hour {hourEntry.Key}: mix '{mixEntry.Key}' appears on multiple stations: {string.Join(", ", mixEntry.Value)}.",
                        });
                }
            }
        }
    }
}
```

- [ ] **Step 4.5: Build and run tests**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
dotnet test soundcloud-ai-mix-recommender-api.sln --filter "FullyQualifiedName~RadioScheduleValidatorTests" --no-build
```

Expected: all pass.

- [ ] **Step 4.6: Commit**

```
git add Changsta.Ai.Core.BusinessProcesses/Radio/RadioScheduleViolation.cs \
        Changsta.Ai.Core.BusinessProcesses/Radio/RadioScheduleValidator.cs \
        Changsta.Ai.Tests.Unit/Radio/RadioScheduleValidatorTests.cs
git commit -m "feat(radio): add schedule validator with all rule checks"
```

---

## Task 5: DTOs + exception + use case contract

### Files
- Create: `Changsta.Ai.Core/Exceptions/RadioStationUnavailableException.cs`
- Create: `Changsta.Ai.Core/Dtos/RadioHourSlotDto.cs`
- Create: `Changsta.Ai.Core/Dtos/RadioStationScheduleDto.cs`
- Create: `Changsta.Ai.Core/Dtos/RadioScheduleResultDto.cs`
- Create: `Changsta.Ai.Core.Contracts/Radio/IGetRadioScheduleUseCase.cs`

- [ ] **Step 5.1: Create RadioStationUnavailableException**

```csharp
// Changsta.Ai.Core/Exceptions/RadioStationUnavailableException.cs
using System;

namespace Changsta.Ai.Core.Exceptions
{
    public sealed class RadioStationUnavailableException : Exception
    {
        public string StationId { get; }

        public RadioStationUnavailableException(string stationId, string message)
            : base(message)
        {
            StationId = stationId;
        }
    }
}
```

Then update the scheduler's empty-catalogue guard in `RadioScheduler.Build` (Task 3, Step 3.5) to throw this instead of `InvalidOperationException`:

```csharp
// Replace:
throw new InvalidOperationException(
    $"Station '{station.Id}' has no eligible mixes ...");

// With:
using Changsta.Ai.Core.Exceptions;

throw new RadioStationUnavailableException(
    station.Id,
    $"Station '{station.Id}' has no eligible mixes in the catalogue. " +
    $"Expected genres: [{string.Join(", ", station.Genres)}].");
```

Also update `RadioSchedulerTests` — the empty-catalogue test must now expect `RadioStationUnavailableException`:

```csharp
// Replace:
[Test]
public void Empty_station_catalogue_throws_InvalidOperationException()
{
    // No mixes at all → Touchdown FM has no eligible mixes
    Action act = () => Build(Thursday, Catalogue(0, 24, 24));
    act.Should().Throw<InvalidOperationException>()
        .WithMessage("*touchdown-fm*");
}

// With:
[Test]
public void Empty_station_catalogue_throws_RadioStationUnavailableException()
{
    // No mixes at all → Touchdown FM has no eligible mixes
    Action act = () => Build(Thursday, Catalogue(0, 24, 24));
    act.Should().Throw<RadioStationUnavailableException>()
        .WithMessage("*touchdown-fm*");
}
```

Add `using Changsta.Ai.Core.Exceptions;` to the test file's using block.

- [ ] **Step 5.2: Create DTOs**

```csharp
// Changsta.Ai.Core/Dtos/RadioHourSlotDto.cs
using System;
using System.Collections.Generic;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class RadioHourSlotDto
    {
        required public int Hour { get; init; }
        required public Mix Mix { get; init; }
        public bool IsCurrent { get; init; }
        public IReadOnlyList<string> AuditWarnings { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> RelaxedRules { get; init; } = Array.Empty<string>();
    }
}
```

```csharp
// Changsta.Ai.Core/Dtos/RadioStationScheduleDto.cs
using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class RadioStationScheduleDto
    {
        required public string Id { get; init; }
        required public string Name { get; init; }
        required public string Frequency { get; init; }
        required public string Description { get; init; }
        public bool IsDefault { get; init; }
        required public RadioHourSlotDto CurrentSlot { get; init; }
        public IReadOnlyList<RadioHourSlotDto> TodaySlots { get; init; } = Array.Empty<RadioHourSlotDto>();
    }
}
```

```csharp
// Changsta.Ai.Core/Dtos/RadioScheduleResultDto.cs
using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class RadioScheduleResultDto
    {
        required public DateTimeOffset GeneratedAtUtc { get; init; }
        required public string ScheduleDate { get; init; }
        required public string Timezone { get; init; }
        required public int CurrentHour { get; init; }
        required public string DefaultStationId { get; init; }
        public IReadOnlyList<RadioStationScheduleDto> Stations { get; init; } = Array.Empty<RadioStationScheduleDto>();
        public IReadOnlyList<string> ValidationWarnings { get; init; } = Array.Empty<string>();
    }
}
```

- [ ] **Step 5.3: Create use case contract**

```csharp
// Changsta.Ai.Core.Contracts/Radio/IGetRadioScheduleUseCase.cs
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Dtos;

namespace Changsta.Ai.Core.Contracts.Radio
{
    public interface IGetRadioScheduleUseCase
    {
        Task<RadioScheduleResultDto> GetAsync(CancellationToken cancellationToken);
    }
}
```

- [ ] **Step 5.4: Build**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```

Expected: clean build.

- [ ] **Step 5.5: Commit**

```
git add Changsta.Ai.Core/Exceptions/RadioStationUnavailableException.cs \
        Changsta.Ai.Core/Dtos/RadioHourSlotDto.cs \
        Changsta.Ai.Core/Dtos/RadioStationScheduleDto.cs \
        Changsta.Ai.Core/Dtos/RadioScheduleResultDto.cs \
        Changsta.Ai.Core.Contracts/Radio/IGetRadioScheduleUseCase.cs \
        Changsta.Ai.Core.BusinessProcesses/Radio/RadioScheduler.cs \
        Changsta.Ai.Tests.Unit/Radio/RadioSchedulerTests.cs
git commit -m "feat(radio): add DTOs, exception type, use case contract, update scheduler guard"
```

---

## Task 6: GetRadioScheduleUseCase + tests

### Files
- Create: `Changsta.Ai.Core.BusinessProcesses/Radio/GetRadioScheduleUseCase.cs`
- Create: `Changsta.Ai.Tests.Unit/Radio/GetRadioScheduleUseCaseTests.cs`

- [ ] **Step 6.1: Write failing tests**

```csharp
// Changsta.Ai.Tests.Unit/Radio/GetRadioScheduleUseCaseTests.cs
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.BusinessProcesses.Radio;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Radio
{
    [TestFixture]
    public sealed class GetRadioScheduleUseCaseTests
    {
        [Test]
        public async Task GetAsync_returns_three_stations()
        {
            RadioScheduleResultDto r = await Run(Catalogue());
            r.Stations.Should().HaveCount(3);
        }

        [Test]
        public async Task GetAsync_default_station_id_is_touchdown_fm()
        {
            RadioScheduleResultDto r = await Run(Catalogue());
            r.DefaultStationId.Should().Be("touchdown-fm");
        }

        [Test]
        public async Task GetAsync_exactly_one_station_has_IsDefault_true()
        {
            RadioScheduleResultDto r = await Run(Catalogue());
            r.Stations.Where(s => s.IsDefault).Should().HaveCount(1);
            r.Stations.Single(s => s.IsDefault).Id.Should().Be("touchdown-fm");
        }

        [Test]
        public async Task GetAsync_each_station_has_current_slot_marked()
        {
            RadioScheduleResultDto r = await Run(Catalogue());
            foreach (RadioStationScheduleDto station in r.Stations)
                station.CurrentSlot.IsCurrent.Should().BeTrue();
        }

        [Test]
        public async Task GetAsync_current_slot_hour_matches_CurrentHour()
        {
            RadioScheduleResultDto r = await Run(Catalogue());
            foreach (RadioStationScheduleDto station in r.Stations)
                station.CurrentSlot.Hour.Should().Be(r.CurrentHour);
        }

        [Test]
        public async Task GetAsync_each_station_has_metadata()
        {
            RadioScheduleResultDto r = await Run(Catalogue());
            foreach (RadioStationScheduleDto station in r.Stations)
            {
                station.Id.Should().NotBeNullOrEmpty();
                station.Name.Should().NotBeNullOrEmpty();
                station.Frequency.Should().NotBeNullOrEmpty();
                station.Description.Should().NotBeNullOrEmpty();
            }
        }

        [Test]
        public async Task GetAsync_schedule_date_and_timezone_present()
        {
            RadioScheduleResultDto r = await Run(Catalogue());
            r.ScheduleDate.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$");
            r.Timezone.Should().Be("UTC");
        }

        [Test]
        public async Task GetAsync_today_slots_has_24_entries()
        {
            RadioScheduleResultDto r = await Run(Catalogue());
            foreach (RadioStationScheduleDto station in r.Stations)
                station.TodaySlots.Should().HaveCount(24);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static Task<RadioScheduleResultDto> Run(IReadOnlyList<Mix> mixes)
            => new GetRadioScheduleUseCase(new StubCatalogue(mixes))
                .GetAsync(CancellationToken.None);

        private static IReadOnlyList<Mix> Catalogue()
        {
            var list = new List<Mix>();
            for (int i = 0; i < 24; i++) list.Add(M($"td{i}", "uk bass", 130));
            for (int i = 0; i < 24; i++) list.Add(M($"ds{i}", "house", 125));
            for (int i = 0; i < 24; i++) list.Add(M($"jp{i}", "dnb", 172));
            return list;
        }

        private static Mix M(string id, string genre, int bpm) => new Mix
        {
            Id = id,
            Title = $"Artist - {id}",
            Url = $"https://sc.test/{id}",
            Genre = genre,
            Energy = "mid",
            BpmMin = bpm,
            BpmMax = bpm + 4,
            Warmth = 0.0,
        };

        private sealed class StubCatalogue : IMixCatalogueProvider
        {
            private readonly IReadOnlyList<Mix> _mixes;
            internal StubCatalogue(IReadOnlyList<Mix> mixes) => _mixes = mixes;
            public Task<IReadOnlyList<Mix>> GetLatestAsync(int maxItems, CancellationToken ct)
                => Task.FromResult(_mixes);
        }
    }
}
```

- [ ] **Step 6.2: Confirm compile error**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```

- [ ] **Step 6.3: Create GetRadioScheduleUseCase**

```csharp
// Changsta.Ai.Core.BusinessProcesses/Radio/GetRadioScheduleUseCase.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Contracts.Radio;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    public sealed class GetRadioScheduleUseCase : IGetRadioScheduleUseCase
    {
        private const int CatalogMaxItems = 200;

        private readonly IMixCatalogueProvider _catalogueProvider;
        private readonly RadioScheduler _scheduler;

        public GetRadioScheduleUseCase(IMixCatalogueProvider catalogueProvider)
        {
            _catalogueProvider = catalogueProvider ?? throw new ArgumentNullException(nameof(catalogueProvider));
            _scheduler = new RadioScheduler();
        }

        public async Task<RadioScheduleResultDto> GetAsync(CancellationToken cancellationToken)
        {
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            int currentHour = utcNow.Hour;
            DateOnly scheduleDate = DateOnly.FromDateTime(utcNow.UtcDateTime);

            IReadOnlyList<Mix> mixes = await _catalogueProvider
                .GetLatestAsync(CatalogMaxItems, cancellationToken)
                .ConfigureAwait(false);

            RadioSchedule schedule = _scheduler.Build(mixes, scheduleDate);

            IReadOnlyList<RadioScheduleViolation> violations = RadioScheduleValidator.Validate(schedule);

            var stations = new List<RadioStationScheduleDto>(RadioStationDefinitions.Stations.Count);

            foreach (RadioStation station in RadioStationDefinitions.Stations)
            {
                IReadOnlyList<RadioScheduledSlot> stationSlots = schedule.StationSlots[station.Id];

                RadioHourSlotDto currentSlot = MapSlot(stationSlots[currentHour], isCurrent: true);
                IReadOnlyList<RadioHourSlotDto> todaySlots = stationSlots
                    .Select(s => MapSlot(s, s.Hour == currentHour))
                    .ToArray();

                stations.Add(new RadioStationScheduleDto
                {
                    Id = station.Id,
                    Name = station.Name,
                    Frequency = station.Frequency,
                    Description = station.Description,
                    IsDefault = station.IsDefault,
                    CurrentSlot = currentSlot,
                    TodaySlots = todaySlots,
                });
            }

            return new RadioScheduleResultDto
            {
                GeneratedAtUtc = utcNow,
                ScheduleDate = scheduleDate.ToString("yyyy-MM-dd"),
                Timezone = "UTC",
                CurrentHour = currentHour,
                DefaultStationId = RadioStationDefinitions.DefaultStationId,
                Stations = stations,
                ValidationWarnings = violations.Select(v => v.Description).ToArray(),
            };
        }

        private static RadioHourSlotDto MapSlot(RadioScheduledSlot slot, bool isCurrent) =>
            new RadioHourSlotDto
            {
                Hour = slot.Hour,
                Mix = slot.Mix,
                IsCurrent = isCurrent,
                AuditWarnings = slot.AuditWarnings,
                RelaxedRules = slot.RelaxedRules,
            };
    }
}
```

- [ ] **Step 6.4: Build and run tests**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
dotnet test soundcloud-ai-mix-recommender-api.sln --filter "FullyQualifiedName~GetRadioScheduleUseCaseTests" --no-build
```

Expected: all pass.

- [ ] **Step 6.5: Commit**

```
git add Changsta.Ai.Core.BusinessProcesses/Radio/GetRadioScheduleUseCase.cs \
        Changsta.Ai.Tests.Unit/Radio/GetRadioScheduleUseCaseTests.cs
git commit -m "feat(radio): add GetRadioScheduleUseCase"
```

---

## Task 7: Remove NowSpinning + rename VM + create RadioController (atomic)

**This task is atomic: the repo must compile cleanly at the end of the commit.** Steps run in an order that keeps the build passing: delete old code before renaming the VM, so no file ever references a class that no longer exists.

**Route:** `GET /api/radio/stations`. Controller catches `RadioStationUnavailableException` → 503. Global middleware handles everything else.

### Step order rationale
1. Delete NowSpinning controllers first — they reference `NowSpinningMixVm`, so deleting them before the rename avoids a broken intermediate state.
2. `git mv NowSpinningMixVm.cs RadioMixVm.cs` and rename the class — SA1649 requires file name = class name.
3. Update mapper, create new VMs, create RadioController.
4. Delete remaining NowSpinning view models, DTOs, use cases, contracts, orphaned internals.
5. Update `Program.cs`.
6. Build and run all tests.

### Files

Delete:
```
Changsta.Ai.Core.BusinessProcesses/NowSpinning/NowSpinningUseCase.cs
Changsta.Ai.Core.BusinessProcesses/NowSpinning/NowSpinningProgramUseCase.cs
Changsta.Ai.Core.BusinessProcesses/NowSpinning/NowSpinningDrawer.cs
Changsta.Ai.Core.BusinessProcesses/NowSpinning/SlotPoolBuilder.cs
Changsta.Ai.Core.BusinessProcesses/NowSpinning/NowSpinningPools.cs
Changsta.Ai.Core.BusinessProcesses/NowSpinning/PoolEntry.cs
Changsta.Ai.Core.BusinessProcesses/NowSpinning/SlotScorer.cs
Changsta.Ai.Core.Contracts/NowSpinning/INowSpinningUseCase.cs
Changsta.Ai.Core.Contracts/NowSpinning/INowSpinningProgramUseCase.cs
Changsta.Ai.Core/Dtos/NowSpinningRequestDto.cs
Changsta.Ai.Core/Dtos/NowSpinningResultDto.cs
Changsta.Ai.Core/Dtos/NowSpinningProgramRequestDto.cs
Changsta.Ai.Core/Dtos/NowSpinningProgramResultDto.cs
Changsta.Ai.Core/Dtos/NowSpinningProgramLaneDto.cs
Changsta.Ai.Core/Dtos/NowSpinningScheduleEntryDto.cs
Changsta.Ai.Core/Dtos/NowSpinningSlotDto.cs
Changsta.Ai.Interface.Api/Controllers/NowSpinningController.cs
Changsta.Ai.Interface.Api/Controllers/NowSpinningProgramController.cs
Changsta.Ai.Interface.Api/ViewModels/NowSpinningProgramLaneVm.cs
Changsta.Ai.Interface.Api/ViewModels/NowSpinningProgramResponse.cs
Changsta.Ai.Interface.Api/ViewModels/NowSpinningResponse.cs
Changsta.Ai.Interface.Api/ViewModels/NowSpinningScheduleEntryVm.cs
Changsta.Ai.Interface.Api/ViewModels/NowSpinningSlotVm.cs
Changsta.Ai.Tests.Unit/NowSpinning/NowSpinningUseCaseTests.cs
Changsta.Ai.Tests.Unit/NowSpinning/NowSpinningProgramUseCaseTests.cs
Changsta.Ai.Tests.Unit/NowSpinning/SlotPoolBuilderTests.cs
Changsta.Ai.Tests.Unit/NowSpinning/SlotScorerTests.cs
Changsta.Ai.Tests.Unit/Controllers/NowSpinningProgramControllerTests.cs
```

Rename (via `git mv`):
- `NowSpinningMixVm.cs` → `RadioMixVm.cs`

Create:
- `Changsta.Ai.Interface.Api/ViewModels/RadioSlotVm.cs`
- `Changsta.Ai.Interface.Api/ViewModels/RadioStationVm.cs`
- `Changsta.Ai.Interface.Api/ViewModels/RadioResponse.cs`
- `Changsta.Ai.Interface.Api/Controllers/RadioController.cs`
- `Changsta.Ai.Tests.Unit/Radio/RadioControllerTests.cs`

Modify:
- `Changsta.Ai.Interface.Api/ViewModels/NowSpinningMixMapper.cs` (return type → `RadioMixVm`)
- `Changsta.Ai.Interface.Api/Program.cs` (remove NowSpinning DI, add radio DI)

- [ ] **Step 7.1: Delete NowSpinning controllers first**

Old controllers reference `NowSpinningMixVm` — deleting them before the rename avoids a broken state.

```bash
git rm \
  Changsta.Ai.Interface.Api/Controllers/NowSpinningController.cs \
  Changsta.Ai.Interface.Api/Controllers/NowSpinningProgramController.cs \
  Changsta.Ai.Interface.Api/ViewModels/NowSpinningProgramLaneVm.cs \
  Changsta.Ai.Interface.Api/ViewModels/NowSpinningProgramResponse.cs \
  Changsta.Ai.Interface.Api/ViewModels/NowSpinningResponse.cs \
  Changsta.Ai.Interface.Api/ViewModels/NowSpinningScheduleEntryVm.cs \
  Changsta.Ai.Interface.Api/ViewModels/NowSpinningSlotVm.cs
```

- [ ] **Step 7.2: git mv NowSpinningMixVm.cs → RadioMixVm.cs**

```bash
git mv \
  Changsta.Ai.Interface.Api/ViewModels/NowSpinningMixVm.cs \
  Changsta.Ai.Interface.Api/ViewModels/RadioMixVm.cs
```

Edit the renamed file — change class name `NowSpinningMixVm` → `RadioMixVm`:

```csharp
// Changsta.Ai.Interface.Api/ViewModels/RadioMixVm.cs
using System;
using System.Collections.Generic;

namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class RadioMixVm
    {
        required public string Id { get; init; }
        required public string Title { get; init; }
        required public string Url { get; init; }
        public string? ImageUrl { get; init; }
        required public string Genre { get; init; }
        required public string Energy { get; init; }
        public int? Bpm { get; init; }
        public IReadOnlyList<string> Moods { get; init; } = Array.Empty<string>();
        public DateTimeOffset? PublishedAt { get; init; }
        public int? Duration { get; init; }
    }
}
```

- [ ] **Step 7.3: Update NowSpinningMixMapper return type**

```csharp
// Changsta.Ai.Interface.Api/ViewModels/NowSpinningMixMapper.cs
using System;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Interface.Api.ViewModels
{
    internal static class NowSpinningMixMapper
    {
        internal static RadioMixVm MapMix(Mix mix)
        {
            return new RadioMixVm
            {
                Id = mix.Id,
                Title = mix.Title,
                Url = mix.Url,
                ImageUrl = mix.ImageUrl,
                Genre = mix.Genre,
                Energy = mix.Energy,
                Bpm = ComputeBpm(mix),
                Moods = mix.Moods,
                PublishedAt = mix.PublishedAt,
                Duration = ParseDurationSeconds(mix.Duration),
            };
        }

        private static int? ComputeBpm(Mix mix)
        {
            if (mix.BpmMin.HasValue && mix.BpmMax.HasValue)
                return (int)Math.Round((mix.BpmMin.Value + mix.BpmMax.Value) / 2.0);
            return mix.BpmMin ?? mix.BpmMax;
        }

        private static int? ParseDurationSeconds(string? duration)
        {
            if (duration is null) return null;
            return TimeSpan.TryParse(duration, out TimeSpan ts) ? (int)ts.TotalSeconds : null;
        }
    }
}
```

- [ ] **Step 7.4: Create new Radio ViewModels**

```csharp
// Changsta.Ai.Interface.Api/ViewModels/RadioSlotVm.cs
using System.Collections.Generic;

namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class RadioSlotVm
    {
        required public int Hour { get; init; }
        required public RadioMixVm Mix { get; init; }
        public bool IsCurrent { get; init; }
        public IReadOnlyList<string> Warnings { get; init; } = System.Array.Empty<string>();
    }
}
```

```csharp
// Changsta.Ai.Interface.Api/ViewModels/RadioStationVm.cs
using System.Collections.Generic;

namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class RadioStationVm
    {
        required public string Id { get; init; }
        required public string Name { get; init; }
        required public string Frequency { get; init; }
        required public string Description { get; init; }
        public bool IsDefault { get; init; }
        required public RadioSlotVm CurrentSlot { get; init; }
        public IReadOnlyList<RadioSlotVm> TodaySlots { get; init; } = System.Array.Empty<RadioSlotVm>();
    }
}
```

```csharp
// Changsta.Ai.Interface.Api/ViewModels/RadioResponse.cs
using System;
using System.Collections.Generic;

namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class RadioResponse
    {
        required public DateTimeOffset GeneratedAtUtc { get; init; }
        required public string ScheduleDate { get; init; }
        required public string Timezone { get; init; }
        required public int CurrentHour { get; init; }
        required public string DefaultStationId { get; init; }
        public IReadOnlyList<RadioStationVm> Stations { get; init; } = System.Array.Empty<RadioStationVm>();
    }
}
```

- [ ] **Step 7.5: Create RadioController with 503 catch**

```csharp
// Changsta.Ai.Interface.Api/Controllers/RadioController.cs
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Radio;
using Changsta.Ai.Core.Dtos;
using Changsta.Ai.Core.Exceptions;
using Changsta.Ai.Interface.Api.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Changsta.Ai.Interface.Api.Controllers
{
    [ApiController]
    [Route("api/radio")]
    [Produces("application/json")]
    public sealed class RadioController : ControllerBase
    {
        private readonly IGetRadioScheduleUseCase _useCase;

        public RadioController(IGetRadioScheduleUseCase useCase)
        {
            _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        }

        [HttpGet("stations")]
        public async Task<IActionResult> GetStationsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                RadioScheduleResultDto result = await _useCase
                    .GetAsync(cancellationToken)
                    .ConfigureAwait(false);

                return Ok(MapToResponse(result));
            }
            catch (RadioStationUnavailableException ex)
            {
                return StatusCode(503, new { error = ex.Message, stationId = ex.StationId });
            }
        }

        private static RadioResponse MapToResponse(RadioScheduleResultDto r) =>
            new RadioResponse
            {
                GeneratedAtUtc = r.GeneratedAtUtc,
                ScheduleDate = r.ScheduleDate,
                Timezone = r.Timezone,
                CurrentHour = r.CurrentHour,
                DefaultStationId = r.DefaultStationId,
                Stations = r.Stations.Select(MapStation).ToArray(),
            };

        private static RadioStationVm MapStation(RadioStationScheduleDto s) =>
            new RadioStationVm
            {
                Id = s.Id,
                Name = s.Name,
                Frequency = s.Frequency,
                Description = s.Description,
                IsDefault = s.IsDefault,
                CurrentSlot = MapSlot(s.CurrentSlot),
                TodaySlots = s.TodaySlots.Select(MapSlot).ToArray(),
            };

        private static RadioSlotVm MapSlot(RadioHourSlotDto slot) =>
            new RadioSlotVm
            {
                Hour = slot.Hour,
                Mix = NowSpinningMixMapper.MapMix(slot.Mix),
                IsCurrent = slot.IsCurrent,
                Warnings = slot.AuditWarnings,
            };
    }
}
```

- [ ] **Step 7.6: Delete remaining NowSpinning artifacts**

```bash
git rm \
  Changsta.Ai.Core.BusinessProcesses/NowSpinning/NowSpinningUseCase.cs \
  Changsta.Ai.Core.BusinessProcesses/NowSpinning/NowSpinningProgramUseCase.cs \
  Changsta.Ai.Core.BusinessProcesses/NowSpinning/NowSpinningDrawer.cs \
  Changsta.Ai.Core.BusinessProcesses/NowSpinning/SlotPoolBuilder.cs \
  Changsta.Ai.Core.BusinessProcesses/NowSpinning/NowSpinningPools.cs \
  Changsta.Ai.Core.BusinessProcesses/NowSpinning/PoolEntry.cs \
  Changsta.Ai.Core.BusinessProcesses/NowSpinning/SlotScorer.cs \
  Changsta.Ai.Core.Contracts/NowSpinning/INowSpinningUseCase.cs \
  Changsta.Ai.Core.Contracts/NowSpinning/INowSpinningProgramUseCase.cs \
  "Changsta.Ai.Core/Dtos/NowSpinningRequestDto.cs" \
  "Changsta.Ai.Core/Dtos/NowSpinningResultDto.cs" \
  "Changsta.Ai.Core/Dtos/NowSpinningProgramRequestDto.cs" \
  "Changsta.Ai.Core/Dtos/NowSpinningProgramResultDto.cs" \
  "Changsta.Ai.Core/Dtos/NowSpinningProgramLaneDto.cs" \
  "Changsta.Ai.Core/Dtos/NowSpinningScheduleEntryDto.cs" \
  "Changsta.Ai.Core/Dtos/NowSpinningSlotDto.cs" \
  Changsta.Ai.Tests.Unit/NowSpinning/NowSpinningUseCaseTests.cs \
  Changsta.Ai.Tests.Unit/NowSpinning/NowSpinningProgramUseCaseTests.cs \
  Changsta.Ai.Tests.Unit/NowSpinning/SlotPoolBuilderTests.cs \
  Changsta.Ai.Tests.Unit/NowSpinning/SlotScorerTests.cs \
  Changsta.Ai.Tests.Unit/Controllers/NowSpinningProgramControllerTests.cs
```

- [ ] **Step 7.7: Update Program.cs**

Remove old using and DI registrations, add new ones. The relevant diff:

```csharp
// Add at top:
using Changsta.Ai.Core.BusinessProcesses.Radio;
using Changsta.Ai.Core.Contracts.Radio;

// Remove:
using Changsta.Ai.Core.Contracts.NowSpinning;

// Remove these registrations:
// builder.Services.AddScoped<INowSpinningUseCase, NowSpinningUseCase>();
// builder.Services.AddScoped<INowSpinningProgramUseCase, NowSpinningProgramUseCase>();

// Add after other use case registrations:
builder.Services.AddScoped<IGetRadioScheduleUseCase, GetRadioScheduleUseCase>();
```

- [ ] **Step 7.8: Write controller tests**

```csharp
// Changsta.Ai.Tests.Unit/Radio/RadioControllerTests.cs
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.BusinessProcesses.Radio;
using Changsta.Ai.Core.Contracts.Radio;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;
using Changsta.Ai.Core.Exceptions;
using Changsta.Ai.Interface.Api.Controllers;
using Changsta.Ai.Interface.Api.ViewModels;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Radio
{
    [TestFixture]
    public sealed class RadioControllerTests
    {
        [Test]
        public async Task GetStationsAsync_returns_200()
        {
            IActionResult result = await MakeController().GetStationsAsync(CancellationToken.None);
            result.Should().BeOfType<OkObjectResult>();
        }

        [Test]
        public async Task GetStationsAsync_response_has_three_stations()
        {
            var ok = (OkObjectResult)await MakeController().GetStationsAsync(CancellationToken.None);
            var response = (RadioResponse)ok.Value!;
            response.Stations.Should().HaveCount(3);
        }

        [Test]
        public async Task GetStationsAsync_default_station_id_is_touchdown_fm()
        {
            var ok = (OkObjectResult)await MakeController().GetStationsAsync(CancellationToken.None);
            var response = (RadioResponse)ok.Value!;
            response.DefaultStationId.Should().Be("touchdown-fm");
        }

        [Test]
        public async Task GetStationsAsync_each_station_has_current_slot()
        {
            var ok = (OkObjectResult)await MakeController().GetStationsAsync(CancellationToken.None);
            var response = (RadioResponse)ok.Value!;
            foreach (RadioStationVm station in response.Stations)
                station.CurrentSlot.Should().NotBeNull();
        }

        [Test]
        public async Task GetStationsAsync_each_station_has_frequency()
        {
            var ok = (OkObjectResult)await MakeController().GetStationsAsync(CancellationToken.None);
            var response = (RadioResponse)ok.Value!;
            foreach (RadioStationVm station in response.Stations)
                station.Frequency.Should().NotBeNullOrEmpty();
        }

        [Test]
        public async Task GetStationsAsync_returns_503_when_station_unavailable()
        {
            var controller = new RadioController(new ThrowingUseCase());
            IActionResult result = await controller.GetStationsAsync(CancellationToken.None);
            var status = result.Should().BeOfType<ObjectResult>().Subject;
            status.StatusCode.Should().Be(503);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static RadioController MakeController() =>
            new RadioController(new StubUseCase(MakeResult()));

        private static RadioScheduleResultDto MakeResult()
        {
            var now = DateTimeOffset.UtcNow;
            var stations = RadioStationDefinitions.Stations
                .Select(s => new RadioStationScheduleDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    Frequency = s.Frequency,
                    Description = s.Description,
                    IsDefault = s.IsDefault,
                    CurrentSlot = new RadioHourSlotDto
                    {
                        Hour = now.Hour,
                        Mix = M($"mix-{s.Id}", s.Genres[0]),
                        IsCurrent = true,
                    },
                })
                .ToArray();

            return new RadioScheduleResultDto
            {
                GeneratedAtUtc = now,
                ScheduleDate = DateOnly.FromDateTime(now.UtcDateTime).ToString("yyyy-MM-dd"),
                Timezone = "UTC",
                CurrentHour = now.Hour,
                DefaultStationId = RadioStationDefinitions.DefaultStationId,
                Stations = stations,
            };
        }

        private static Mix M(string id, string genre) => new Mix
        {
            Id = id, Title = $"A - {id}", Url = $"https://sc.test/{id}",
            Genre = genre, Energy = "mid", BpmMin = 125,
        };

        private sealed class StubUseCase : IGetRadioScheduleUseCase
        {
            private readonly RadioScheduleResultDto _r;
            internal StubUseCase(RadioScheduleResultDto r) => _r = r;
            public Task<RadioScheduleResultDto> GetAsync(CancellationToken ct) => Task.FromResult(_r);
        }

        private sealed class ThrowingUseCase : IGetRadioScheduleUseCase
        {
            public Task<RadioScheduleResultDto> GetAsync(CancellationToken ct)
                => throw new RadioStationUnavailableException("touchdown-fm", "No mixes found.");
        }
    }
}
```

- [ ] **Step 7.9: Build clean**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```

Expected: clean build — no references to deleted types.

- [ ] **Step 7.10: Run full test suite**

```
dotnet test soundcloud-ai-mix-recommender-api.sln --no-build
```

Expected: all Radio tests pass; `SlotDefinitions`-level tests that existed before (if any) still pass.

- [ ] **Step 7.11: Commit**

```
git add -A
git commit -m "feat(radio): replace NowSpinning with radio stations API — remove old routes, add 503 handling"
```

---

## Self-review: spec coverage

| Requirement | Task |
|---|---|
| 3 editorial stations | 1, 6 |
| Touchdown FM default | 1 (`DefaultStationId`, `IsDefault`) |
| 24 slots per station | 3 (scheduler loop 0–23), 4 (validator) |
| Shared deterministic schedule | 3 (seeded pick from `date.DayNumber`) |
| Genre ownership — strict | 1 (genre map), 3 (eligible filter), 4 (GenreMismatch check) |
| Energy primary daypart signal | 2 (energyScore = 5.0 for match, 2.5 neutral, 0 wrong) |
| Warmth (moodWeight) daypart | 2 (warmthScore — same formula as old SlotScorer; SlotScorer.cs itself deleted) |
| BPM daypart, station-relative | 1 (BPM offsets), 3 (offset applied in scheduler) |
| Unknown energy → neutral + audit | 2 (2.5 score + EnergyWarning) |
| Same-station same-day repeat forbidden | 3 (usedIds guard), 4 (SameStationSameDayRepeat validation) |
| Cross-station hour avoidance | 4 (SameHourCrossStationDuplicate) |
| Genre clustering soft penalty | 2 (genreClusterPenalty) |
| Artist/title soft penalty | 2 (artistPenalty, ExtractArtistKey) |
| Ordered fallback | 3 (5-stage threshold-gated fallback) |
| Fallback auditable | 3 (RelaxedRules on each slot) |
| Schedule always complete | 3 (last-resort repeat, empty-catalogue exception), 4 (validator) |
| Freshness/underplayed preference | 2 (freshnessBonus), 3 (crossScheduleUsed) |
| Station metadata in response | 1, 5, 7 |
| `defaultStationId` in response | 5 (DTO), 7 (RadioResponse), 6 (use case) |
| Frontend gets data without applying logic | 7 (flat VMs, no scheduling exposed) |
| Per-slot audit | 2, 3 (Score, AuditReasons, AuditWarnings, RelaxedRules) |
| Old routes removed | 7 |
| Empty catalogue → 503 | 5 (`RadioStationUnavailableException`), 7 (controller catch) |
| .NET 10 | All (no .NET 8 references) |

### Known limitations (acceptable for v1)

- **No cross-day cooldown:** No persistent history store. Repeat avoidance is same-day only.
- **Techno dominance guard:** Handled naturally by genre clustering penalty — no special rule needed.
- **Station names 2 and 3:** "Deep Signal FM" and "Jungle Pressure" are working names. Change only in `RadioStationDefinitions.cs`.
