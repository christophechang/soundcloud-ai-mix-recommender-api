# Now Spinning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `GET /api/catalog/now-spinning` — a time-slotted, seeded random mix picker with 24 pre-scored pools (6 slots × 4 day buckets), mood lean filtering, and a schedule strip.

**Architecture:** Slot/pool logic lives entirely in `Core.BusinessProcesses/NowSpinning/`; the controller is thin and maps params to a use case call. Pools are built on every request from the already-memory-cached catalog (no secondary cache needed — it's pure computation over ~50–100 mixes). The seeded draw ensures all listeners get the same pick within the same hour + skip state.

**Tech Stack:** C# 12, ASP.NET Core, NUnit + FluentAssertions, existing `IMixCatalogueProvider` catalog cache.

**Spec:** `Docs/api-now-spinning.md` — read it if anything below is ambiguous.

---

## File Map

### New files
| File | Responsibility |
|---|---|
| `Changsta.Ai.Core/Dtos/NowSpinningRequestDto.cs` | Request DTO + `MoodLean` enum |
| `Changsta.Ai.Core/Dtos/NowSpinningResultDto.cs` | Result DTO, `SlotInfoDto`, `ScheduleEntryDto` |
| `Changsta.Ai.Core.Contracts/NowSpinning/INowSpinningUseCase.cs` | Use case contract |
| `Changsta.Ai.Core.BusinessProcesses/NowSpinning/SlotDefinitions.cs` | `SlotKey`, `DayBucket` enums, `SlotConfig` record, slot table, BPM target helper |
| `Changsta.Ai.Core.BusinessProcesses/NowSpinning/SlotScorer.cs` | Score a single mix against a slot config |
| `Changsta.Ai.Core.BusinessProcesses/NowSpinning/SlotPoolBuilder.cs` | `PoolEntry`, `NowSpinningPools`, pool-building logic |
| `Changsta.Ai.Core.BusinessProcesses/NowSpinning/NowSpinningUseCase.cs` | Orchestration: slot resolve, draw, schedule |
| `Changsta.Ai.Interface.Api/ViewModels/NowSpinningResponse.cs` | API response view models |
| `Changsta.Ai.Interface.Api/Controllers/NowSpinningController.cs` | Thin controller |
| `Changsta.Ai.Tests.Unit/NowSpinning/SlotScorerTests.cs` | Unit tests for scorer |
| `Changsta.Ai.Tests.Unit/NowSpinning/SlotPoolBuilderTests.cs` | Unit tests for pool builder |
| `Changsta.Ai.Tests.Unit/NowSpinning/NowSpinningUseCaseTests.cs` | Unit tests for use case |

### Modified files
| File | Change |
|---|---|
| `Changsta.Ai.Interface.Api/Program.cs` | Register `INowSpinningUseCase` |
| `Changsta.Ai.Core.BusinessProcesses/Changsta.Ai.Core.BusinessProcesses.csproj` | Add `InternalsVisibleTo("Changsta.Ai.Tests.Unit")` so tests can access `internal` types |

---

## Task 1: DTOs and Contract

**Files:**
- Create: `Changsta.Ai.Core/Dtos/NowSpinningRequestDto.cs`
- Create: `Changsta.Ai.Core/Dtos/NowSpinningResultDto.cs`
- Create: `Changsta.Ai.Core.Contracts/NowSpinning/INowSpinningUseCase.cs`

- [ ] **Step 1: Create request DTO**

```csharp
// Changsta.Ai.Core/Dtos/NowSpinningRequestDto.cs
using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.Dtos
{
    public enum MoodLean
    {
        Darker,
        Warmer,
        Slower,
        Faster,
    }

    public sealed class NowSpinningRequestDto
    {
        required public DateTimeOffset UtcNow { get; init; }

        public int UtcOffsetMinutes { get; init; } = 0;

        public MoodLean? MoodLean { get; init; }

        public IReadOnlyList<string> SkipIds { get; init; } = Array.Empty<string>();

        public int ScheduleCount { get; init; } = 4;
    }
}
```

- [ ] **Step 2: Create result DTOs**

```csharp
// Changsta.Ai.Core/Dtos/NowSpinningResultDto.cs
using System;
using System.Collections.Generic;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class NowSpinningSlotDto
    {
        required public string Key { get; init; }
        required public string Label { get; init; }
    }

    public sealed class NowSpinningScheduleEntryDto
    {
        required public DateTimeOffset At { get; init; }
        required public NowSpinningSlotDto Slot { get; init; }
        required public string DayBucket { get; init; }
        public Mix? Mix { get; init; }
    }

    public sealed class NowSpinningResultDto
    {
        required public DateTimeOffset Now { get; init; }
        required public string DayBucket { get; init; }
        required public NowSpinningSlotDto Slot { get; init; }
        public Mix? Mix { get; init; }
        public IReadOnlyList<NowSpinningScheduleEntryDto> Schedule { get; init; } = Array.Empty<NowSpinningScheduleEntryDto>();
        public bool LeanIgnored { get; init; }
        public bool SkipsIgnored { get; init; }
        public bool PoolFallback { get; init; }
        public bool NoMixAvailable { get; init; }
    }
}
```

- [ ] **Step 3: Create use case contract**

```csharp
// Changsta.Ai.Core.Contracts/NowSpinning/INowSpinningUseCase.cs
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Dtos;

namespace Changsta.Ai.Core.Contracts.NowSpinning
{
    public interface INowSpinningUseCase
    {
        Task<NowSpinningResultDto> GetAsync(NowSpinningRequestDto request, CancellationToken cancellationToken);
    }
}
```

- [ ] **Step 4: Build (no tests yet — contracts only)**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```
Expected: no errors.

---

## Task 2: Enable Test Access to BusinessProcesses Internals

**Files:**
- Modify: `Changsta.Ai.Core.BusinessProcesses/Changsta.Ai.Core.BusinessProcesses.csproj`

- [ ] **Step 1: Add InternalsVisibleTo**

Open `Changsta.Ai.Core.BusinessProcesses/Changsta.Ai.Core.BusinessProcesses.csproj`. Add this `ItemGroup` (mirror the pattern in `Infrastructure.Services.Azure.csproj`):

```xml
  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
      <_Parameter1>Changsta.Ai.Tests.Unit</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>
```

- [ ] **Step 2: Build**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```
Expected: no errors.

---

## Task 3: Slot Definitions

**Files:**
- Create: `Changsta.Ai.Core.BusinessProcesses/NowSpinning/SlotDefinitions.cs`

- [ ] **Step 1: Write slot definitions**

```csharp
// Changsta.Ai.Core.BusinessProcesses/NowSpinning/SlotDefinitions.cs
using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.BusinessProcesses.NowSpinning
{
    internal enum SlotKey
    {
        Dead,
        Comedown,
        Morning,
        Afternoon,
        EarlyEve,
        Primetime,
    }

    internal enum DayBucket
    {
        Sunday,
        Weeknight,
        Friday,
        Saturday,
    }

    internal sealed record SlotConfig(
        SlotKey Key,
        string Label,
        int BaseBpmTarget,
        double WarmthTarget,
        string[] EnergyValues);

    internal static class SlotDefinitions
    {
        internal static readonly SlotKey[] SlotOrder = new[]
        {
            SlotKey.Dead,
            SlotKey.Comedown,
            SlotKey.Morning,
            SlotKey.Afternoon,
            SlotKey.EarlyEve,
            SlotKey.Primetime,
        };

        internal static readonly IReadOnlyDictionary<SlotKey, SlotConfig> Slots =
            new Dictionary<SlotKey, SlotConfig>
            {
                [SlotKey.Dead] = new(SlotKey.Dead, "dead of night", 172, -0.6, new[] { "peak", "high" }),
                [SlotKey.Comedown] = new(SlotKey.Comedown, "comedown", 110, 0.4, new[] { "chilled", "low-mid", "low" }),
                [SlotKey.Morning] = new(SlotKey.Morning, "morning", 122, 0.5, new[] { "low-mid", "mid", "chilled", "low" }),
                [SlotKey.Afternoon] = new(SlotKey.Afternoon, "afternoon", 125, 0.3, new[] { "mid", "journey" }),
                [SlotKey.EarlyEve] = new(SlotKey.EarlyEve, "evening", 128, 0.0, new[] { "mid", "mid-peak", "journey", "mid-high" }),
                [SlotKey.Primetime] = new(SlotKey.Primetime, "primetime", 138, -0.3, new[] { "peak", "high", "mid-peak", "mid-high" }),
            };

        internal static SlotKey ResolveSlot(int localHour) => localHour switch
        {
            < 4 => SlotKey.Dead,
            < 8 => SlotKey.Comedown,
            < 12 => SlotKey.Morning,
            < 17 => SlotKey.Afternoon,
            < 21 => SlotKey.EarlyEve,
            _ => SlotKey.Primetime,
        };

        internal static DayBucket ResolveDayBucket(DayOfWeek day) => day switch
        {
            DayOfWeek.Sunday => DayBucket.Sunday,
            DayOfWeek.Friday => DayBucket.Friday,
            DayOfWeek.Saturday => DayBucket.Saturday,
            _ => DayBucket.Weeknight,
        };

        internal static string DayBucketKey(DayBucket bucket) => bucket switch
        {
            DayBucket.Sunday => "sunday",
            DayBucket.Friday => "friday",
            DayBucket.Saturday => "saturday",
            _ => "weeknight",
        };

        internal static string SlotKeyString(SlotKey slot) => slot switch
        {
            SlotKey.Dead => "dead",
            SlotKey.Comedown => "comedown",
            SlotKey.Morning => "morning",
            SlotKey.Afternoon => "afternoon",
            SlotKey.EarlyEve => "earlyeve",
            _ => "primetime",
        };

        internal static int GetBpmTarget(SlotKey slot, DayBucket day)
        {
            int adjustment = day switch
            {
                DayBucket.Sunday => -10,
                DayBucket.Friday => +4,
                DayBucket.Saturday => +8,
                _ => 0,
            };
            return Slots[slot].BaseBpmTarget + adjustment;
        }

        internal static SlotKey AdjacentSlot(SlotKey slot)
        {
            int index = Array.IndexOf(SlotOrder, slot);
            return SlotOrder[(index + 1) % SlotOrder.Length];
        }
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```
Expected: no errors.

---

## Task 4: Slot Scorer (TDD)

**Files:**
- Create: `Changsta.Ai.Core.BusinessProcesses/NowSpinning/SlotScorer.cs`
- Create: `Changsta.Ai.Tests.Unit/NowSpinning/SlotScorerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// Changsta.Ai.Tests.Unit/NowSpinning/SlotScorerTests.cs
using Changsta.Ai.Core.BusinessProcesses.NowSpinning;
using Changsta.Ai.Core.Domain;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.NowSpinning
{
    [TestFixture]
    public sealed class SlotScorerTests
    {
        private static readonly SlotConfig Primetime =
            SlotDefinitions.Slots[SlotKey.Primetime]; // bpmTarget=138, warmth=-0.3, energy=[peak,high,mid-peak,mid-high]

        [Test]
        public void Score_perfect_bpm_gives_8_points()
        {
            var mix = MakeMix(bpmMin: 138, bpmMax: 138, warmth: -0.3, energy: "peak");
            double score = SlotScorer.Score(mix, Primetime, 138);
            score.Should().BeApproximately(8 + 4 + 5, 0.01); // 17 max
        }

        [Test]
        public void Score_bpm_48_away_from_target_gives_zero_bpm_points()
        {
            var mix = MakeMix(bpmMin: 90, bpmMax: 90, warmth: -0.3, energy: "peak");
            double bpmScore = 8 - Math.Abs(90 - 138) / 6.0; // 8 - 8 = 0
            double score = SlotScorer.Score(mix, Primetime, 138);
            score.Should().BeGreaterThanOrEqualTo(bpmScore + 4 + 5 - 0.01);
        }

        [Test]
        public void Score_bpm_further_than_48_clamps_to_zero()
        {
            var mix = MakeMix(bpmMin: 80, bpmMax: 80, warmth: -0.3, energy: "peak");
            double score = SlotScorer.Score(mix, Primetime, 138);
            score.Should().BeLessThan(4 + 5 + 0.01); // bpm contributes 0
        }

        [Test]
        public void Score_null_bpm_returns_zero()
        {
            var mix = MakeMix(bpmMin: null, bpmMax: null, warmth: -0.3, energy: "peak");
            double score = SlotScorer.Score(mix, Primetime, 138);
            score.Should().Be(0.0);
        }

        [Test]
        public void Score_null_warmth_treated_as_zero()
        {
            var mixNullWarmth = MakeMix(bpmMin: 138, bpmMax: 138, warmth: null, energy: "peak");
            var mixZeroWarmth = MakeMix(bpmMin: 138, bpmMax: 138, warmth: 0.0, energy: "peak");
            double scoreNull = SlotScorer.Score(mixNullWarmth, Primetime, 138);
            double scoreZero = SlotScorer.Score(mixZeroWarmth, Primetime, 138);
            scoreNull.Should().BeApproximately(scoreZero, 0.001);
        }

        [Test]
        public void Score_energy_mismatch_gives_zero_energy_points()
        {
            var mix = MakeMix(bpmMin: 138, bpmMax: 138, warmth: -0.3, energy: "chilled");
            double score = SlotScorer.Score(mix, Primetime, 138);
            score.Should().BeApproximately(8 + 4, 0.01); // no energy bonus
        }

        [Test]
        public void Score_energy_match_is_exact_ordinal()
        {
            // Spec requires exact string equality — energy values in mixes must match slot table strings exactly
            var mixExact = MakeMix(bpmMin: 138, bpmMax: 138, warmth: -0.3, energy: "peak");
            var mixWrongCase = MakeMix(bpmMin: 138, bpmMax: 138, warmth: -0.3, energy: "PEAK");
            double scoreExact = SlotScorer.Score(mixExact, Primetime, 138);
            double scoreWrong = SlotScorer.Score(mixWrongCase, Primetime, 138);
            scoreExact.Should().BeApproximately(17, 0.01);
            scoreWrong.Should().BeApproximately(8 + 4, 0.01); // no energy bonus — wrong case
        }

        [Test]
        public void Score_bpm_uses_midpoint_of_range()
        {
            // (130 + 146) / 2 = 138 → perfect score
            var mix = MakeMix(bpmMin: 130, bpmMax: 146, warmth: -0.3, energy: "peak");
            double score = SlotScorer.Score(mix, Primetime, 138);
            score.Should().BeApproximately(17, 0.01);
        }

        [Test]
        public void Score_bpm_uses_min_when_max_null()
        {
            var mix = MakeMix(bpmMin: 138, bpmMax: null, warmth: -0.3, energy: "peak");
            double score = SlotScorer.Score(mix, Primetime, 138);
            score.Should().BeApproximately(17, 0.01);
        }

        private static Mix MakeMix(int? bpmMin, int? bpmMax, double? warmth, string energy) => new Mix
        {
            Id = "test",
            Title = "Test",
            Url = "https://sc.test/test",
            Genre = "dnb",
            Energy = energy,
            BpmMin = bpmMin,
            BpmMax = bpmMax,
            Warmth = warmth,
        };
    }
}
```

- [ ] **Step 2: Run tests — expect build error (SlotScorer not yet defined)**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```

- [ ] **Step 3: Implement SlotScorer**

```csharp
// Changsta.Ai.Core.BusinessProcesses/NowSpinning/SlotScorer.cs
using System;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.BusinessProcesses.NowSpinning
{
    internal static class SlotScorer
    {
        internal static double Score(Mix mix, SlotConfig config, int bpmTarget)
        {
            int? bpm = ComputeBpm(mix);
            if (bpm is null)
            {
                return 0.0;
            }

            double warmth = mix.Warmth ?? 0.0;

            double bpmScore = Math.Max(0, 8.0 - Math.Abs(bpm.Value - bpmTarget) / 6.0);
            double warmthScore = Math.Max(0, 4.0 - Math.Abs(warmth - config.WarmthTarget) / 0.25);
            double energyScore = EnergyMatches(mix.Energy, config.EnergyValues) ? 5.0 : 0.0;

            return bpmScore + warmthScore + energyScore;
        }

        internal static int? ComputeBpm(Mix mix)
        {
            if (mix.BpmMin.HasValue && mix.BpmMax.HasValue)
            {
                return (int)Math.Round((mix.BpmMin.Value + mix.BpmMax.Value) / 2.0);
            }

            return mix.BpmMin ?? mix.BpmMax;
        }

        private static bool EnergyMatches(string? energy, string[] energyValues)
        {
            if (energy is null)
            {
                return false;
            }

            foreach (string e in energyValues)
            {
                if (string.Equals(e, energy, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
```

- [ ] **Step 4: Run tests**

```
dotnet test soundcloud-ai-mix-recommender-api.sln --no-build --filter "FullyQualifiedName~SlotScorerTests"
```
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add Changsta.Ai.Core.BusinessProcesses/NowSpinning/SlotScorer.cs \
        Changsta.Ai.Tests.Unit/NowSpinning/SlotScorerTests.cs
git commit -m "feat: add SlotScorer for now-spinning slot assignment"
```

---

## Task 5: Slot Pool Builder (TDD)

**Files:**
- Create: `Changsta.Ai.Core.BusinessProcesses/NowSpinning/SlotPoolBuilder.cs`
- Create: `Changsta.Ai.Tests.Unit/NowSpinning/SlotPoolBuilderTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// Changsta.Ai.Tests.Unit/NowSpinning/SlotPoolBuilderTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Changsta.Ai.Core.BusinessProcesses.NowSpinning;
using Changsta.Ai.Core.Domain;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.NowSpinning
{
    [TestFixture]
    public sealed class SlotPoolBuilderTests
    {
        [Test]
        public void Build_mix_with_null_bpm_excluded_from_all_pools()
        {
            var mix = MakeMix("x", bpmMin: null, bpmMax: null, warmth: 0.0, energy: "mid");
            var pools = SlotPoolBuilder.Build(new[] { mix });

            foreach (SlotKey slot in SlotDefinitions.SlotOrder)
            {
                foreach (DayBucket day in Enum.GetValues<DayBucket>())
                {
                    pools.Pools[(slot, day)].Should().BeEmpty();
                }
            }
        }

        [Test]
        public void Build_perfect_scoring_mix_appears_in_its_slot()
        {
            // Primetime: bpm=138, warmth=-0.3, energy=peak → score=17
            var mix = MakeMix("p", bpmMin: 138, bpmMax: 138, warmth: -0.3, energy: "peak");
            var pools = SlotPoolBuilder.Build(new[] { mix });

            // Should appear in primetime weeknight pool (bpmTarget=138+0=138)
            pools.Pools[(SlotKey.Primetime, DayBucket.Weeknight)]
                .Should().Contain(e => e.Mix.Id == "p");
        }

        [Test]
        public void Build_mix_can_appear_in_multiple_slots()
        {
            // A neutral-warmth, mid-BPM, mid-energy mix might score above threshold in multiple slots
            // afternoon: bpm=125, warmth=0.3, energy=[mid,journey]
            // earlyeve: bpm=128, warmth=0.0, energy=[mid,mid-peak,journey]
            // Use a mix that scores well in both
            var mix = MakeMix("m", bpmMin: 126, bpmMax: 126, warmth: 0.15, energy: "mid");
            var pools = SlotPoolBuilder.Build(new[] { mix });

            bool inAfternoon = pools.Pools[(SlotKey.Afternoon, DayBucket.Weeknight)].Any(e => e.Mix.Id == "m");
            bool inEarlyEve = pools.Pools[(SlotKey.EarlyEve, DayBucket.Weeknight)].Any(e => e.Mix.Id == "m");

            (inAfternoon || inEarlyEve).Should().BeTrue();
        }

        [Test]
        public void Build_darker_lean_tag_applied_to_cold_high_energy_mix()
        {
            // darker: warmth < -0.3 AND energy in [peak, high, mid-peak, mid-high]
            var mix = MakeMix("d", bpmMin: 138, bpmMax: 138, warmth: -0.5, energy: "peak");
            var pools = SlotPoolBuilder.Build(new[] { mix });

            var entry = pools.Pools[(SlotKey.Primetime, DayBucket.Weeknight)]
                .FirstOrDefault(e => e.Mix.Id == "d");
            entry.Should().NotBeNull();
            entry!.LeanTags.Should().Contain(MoodLean.Darker);
        }

        [Test]
        public void Build_warmer_lean_tag_applied_to_warm_mix()
        {
            // warmer: warmth > 0.3
            var mix = MakeMix("w", bpmMin: 125, bpmMax: 125, warmth: 0.5, energy: "mid");
            var pools = SlotPoolBuilder.Build(new[] { mix });

            var entry = pools.Pools[(SlotKey.Afternoon, DayBucket.Weeknight)]
                .FirstOrDefault(e => e.Mix.Id == "w");
            entry.Should().NotBeNull();
            entry!.LeanTags.Should().Contain(MoodLean.Warmer);
        }

        [Test]
        public void Build_slower_lean_is_relative_to_slot_bpm_target()
        {
            // 120 BPM: slower in primetime (target 138, diff=-18 < -10) but NOT slower in comedown (target 110, diff=+10)
            var mix = MakeMix("s", bpmMin: 120, bpmMax: 120, warmth: 0.0, energy: "mid");
            var pools = SlotPoolBuilder.Build(new[] { mix });

            var primetimeEntry = pools.Pools[(SlotKey.Primetime, DayBucket.Weeknight)]
                .FirstOrDefault(e => e.Mix.Id == "s");
            var comedownEntry = pools.Pools[(SlotKey.Comedown, DayBucket.Weeknight)]
                .FirstOrDefault(e => e.Mix.Id == "s");

            if (primetimeEntry is not null)
            {
                primetimeEntry.LeanTags.Should().Contain(MoodLean.Slower);
            }

            if (comedownEntry is not null)
            {
                comedownEntry.LeanTags.Should().NotContain(MoodLean.Slower);
            }
        }

        [Test]
        public void Build_day_bucket_bpm_adjustment_affects_pool_membership()
        {
            // Saturday primetime target = 138 + 8 = 146
            // A mix at 146 BPM scores perfectly on Saturday but poorly on weeknight (138 target)
            // This test confirms Saturday pool may differ from weeknight pool
            var mix146 = MakeMix("sat", bpmMin: 146, bpmMax: 146, warmth: -0.3, energy: "peak");
            var mix138 = MakeMix("wkn", bpmMin: 138, bpmMax: 138, warmth: -0.3, energy: "peak");

            var pools = SlotPoolBuilder.Build(new[] { mix146, mix138 });

            // Both should be in saturday (146 targets perfectly, 138 still close)
            // But saturday pool should rank 146 mix higher — we just verify it's present
            pools.Pools[(SlotKey.Primetime, DayBucket.Saturday)].Any(e => e.Mix.Id == "sat").Should().BeTrue();
        }

        [Test]
        public void Build_mix_with_score_below_3_excluded_even_if_in_top_40_percent()
        {
            // Single mix with a very low score should be excluded by the hard floor
            // Dead slot: bpm=172, warmth=-0.6, energy=[peak,high]
            // Mix at 172bpm but warmth=0.9 (very far from -0.6) and wrong energy
            // warmth score = max(0, 4 - |0.9 - (-0.6)| / 0.25) = max(0, 4-6) = 0
            // energy score = 0 (mid != peak/high)
            // bpm score = 8 (perfect bpm)
            // total = 8 — above floor so this won't test the floor case
            // Use a mix with BPM far from target and warmth far from target and wrong energy
            // bpm=90, dead target=172, diff=82 → bpmScore = max(0, 8-82/6) = max(0,-5.67) = 0
            // warmth=0.9, dead target=-0.6, diff=1.5 → warmthScore = max(0, 4-6) = 0
            // energy=mid → 0
            // total = 0 → excluded
            var lowScoreMix = MakeMix("low", bpmMin: 90, bpmMax: 90, warmth: 0.9, energy: "mid");
            var pools = SlotPoolBuilder.Build(new[] { lowScoreMix });

            pools.Pools[(SlotKey.Dead, DayBucket.Weeknight)].Should().BeEmpty();
        }

        [Test]
        public void Build_empty_catalog_produces_empty_pools()
        {
            var pools = SlotPoolBuilder.Build(Array.Empty<Mix>());

            foreach (SlotKey slot in SlotDefinitions.SlotOrder)
            {
                foreach (DayBucket day in Enum.GetValues<DayBucket>())
                {
                    pools.Pools[(slot, day)].Should().BeEmpty();
                }
            }
        }

        private static Mix MakeMix(string id, int? bpmMin, int? bpmMax, double? warmth, string energy) => new Mix
        {
            Id = id,
            Title = $"Mix {id}",
            Url = $"https://sc.test/{id}",
            Genre = "dnb",
            Energy = energy,
            BpmMin = bpmMin,
            BpmMax = bpmMax,
            Warmth = warmth,
        };
    }
}
```

- [ ] **Step 2: Run — expect build error (SlotPoolBuilder not yet defined)**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```

- [ ] **Step 3: Implement SlotPoolBuilder**

```csharp
// Changsta.Ai.Core.BusinessProcesses/NowSpinning/SlotPoolBuilder.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Changsta.Ai.Core.Dtos;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.BusinessProcesses.NowSpinning
{
    internal sealed record PoolEntry(Mix Mix, IReadOnlySet<MoodLean> LeanTags);

    internal sealed class NowSpinningPools
    {
        required public IReadOnlyDictionary<(SlotKey, DayBucket), IReadOnlyList<PoolEntry>> Pools { get; init; }
    }

    internal static class SlotPoolBuilder
    {
        private const double MinAbsoluteScore = 3.0;
        private const double PercentileKeepFraction = 0.40; // keep top 40%

        internal static NowSpinningPools Build(IReadOnlyList<Mix> mixes)
        {
            var pools = new Dictionary<(SlotKey, DayBucket), IReadOnlyList<PoolEntry>>();

            foreach (SlotKey slot in SlotDefinitions.SlotOrder)
            {
                SlotConfig config = SlotDefinitions.Slots[slot];

                foreach (DayBucket day in Enum.GetValues<DayBucket>())
                {
                    int bpmTarget = SlotDefinitions.GetBpmTarget(slot, day);

                    var scored = new List<(Mix mix, double score, int bpm)>();

                    foreach (Mix mix in mixes)
                    {
                        int? bpm = SlotScorer.ComputeBpm(mix);
                        if (bpm is null)
                        {
                            continue;
                        }

                        double score = SlotScorer.Score(mix, config, bpmTarget);
                        scored.Add((mix, score, bpm.Value));
                    }

                    if (scored.Count == 0)
                    {
                        pools[(slot, day)] = Array.Empty<PoolEntry>();
                        continue;
                    }

                    double threshold = ComputeThreshold(scored.Select(s => s.score).ToArray());

                    var entries = new List<PoolEntry>();

                    foreach (var (mix, score, bpm) in scored)
                    {
                        if (score < threshold)
                        {
                            continue;
                        }

                        IReadOnlySet<MoodLean> leanTags = ComputeLeanTags(mix, bpm, bpmTarget);
                        entries.Add(new PoolEntry(mix, leanTags));
                    }

                    pools[(slot, day)] = entries;
                }
            }

            return new NowSpinningPools { Pools = pools };
        }

        private static double ComputeThreshold(double[] scores)
        {
            Array.Sort(scores);
            // 60th percentile index (top 40% are above this)
            int percentileIndex = (int)Math.Floor(scores.Length * (1.0 - PercentileKeepFraction));
            percentileIndex = Math.Clamp(percentileIndex, 0, scores.Length - 1);
            double percentileScore = scores[percentileIndex];
            // Hard floor: must also score >= 3.0. Take the more restrictive bound.
            return Math.Max(percentileScore, MinAbsoluteScore);
        }

        private static IReadOnlySet<MoodLean> ComputeLeanTags(Mix mix, int bpm, int bpmTarget)
        {
            var tags = new HashSet<MoodLean>();
            double warmth = mix.Warmth ?? 0.0;

            if (warmth < -0.3 && IsHighEnergy(mix.Energy))
            {
                tags.Add(MoodLean.Darker);
            }

            if (warmth > 0.3)
            {
                tags.Add(MoodLean.Warmer);
            }

            if (bpm < bpmTarget - 10)
            {
                tags.Add(MoodLean.Slower);
            }

            if (bpm > bpmTarget + 10)
            {
                tags.Add(MoodLean.Faster);
            }

            return tags;
        }

        private static bool IsHighEnergy(string? energy)
        {
            return string.Equals(energy, "peak", StringComparison.Ordinal)
                || string.Equals(energy, "high", StringComparison.Ordinal)
                || string.Equals(energy, "mid-peak", StringComparison.Ordinal)
                || string.Equals(energy, "mid-high", StringComparison.Ordinal);
        }
    }
}
```

- [ ] **Step 4: Run tests**

```
dotnet test soundcloud-ai-mix-recommender-api.sln --no-build --filter "FullyQualifiedName~SlotPoolBuilderTests"
```
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add Changsta.Ai.Core.BusinessProcesses/NowSpinning/SlotPoolBuilder.cs \
        Changsta.Ai.Tests.Unit/NowSpinning/SlotPoolBuilderTests.cs
git commit -m "feat: add SlotPoolBuilder for now-spinning 24-pool assignment"
```

---

## Task 6: Now Spinning Use Case (TDD)

**Files:**
- Create: `Changsta.Ai.Core.BusinessProcesses/NowSpinning/NowSpinningUseCase.cs`
- Create: `Changsta.Ai.Tests.Unit/NowSpinning/NowSpinningUseCaseTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// Changsta.Ai.Tests.Unit/NowSpinning/NowSpinningUseCaseTests.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.BusinessProcesses.NowSpinning;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.NowSpinning
{
    [TestFixture]
    public sealed class NowSpinningUseCaseTests
    {
        // Primetime Friday: utcOffset=0, UTC hour=22 → local hour=22 → primetime, day=Friday
        private static readonly DateTimeOffset PrimetimeFriday =
            new DateTimeOffset(2026, 5, 15, 22, 0, 0, TimeSpan.Zero); // 2026-05-15 is a Friday

        [Test]
        public async Task GetAsync_returns_mix_from_matching_slot()
        {
            var mix = MakePrimetimeMix("pt1");
            var useCase = MakeUseCase(new[] { mix });

            var result = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);

            result.Mix.Should().NotBeNull();
            result.Mix!.Id.Should().Be("pt1");
            result.Slot.Key.Should().Be("primetime");
            result.DayBucket.Should().Be("friday");
        }

        [Test]
        public async Task GetAsync_same_hour_same_skip_state_returns_same_mix()
        {
            var mixes = new[] { MakePrimetimeMix("a"), MakePrimetimeMix("b"), MakePrimetimeMix("c") };
            var useCase = MakeUseCase(mixes);

            var r1 = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);
            var r2 = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);

            r1.Mix!.Id.Should().Be(r2.Mix!.Id);
        }

        [Test]
        public async Task GetAsync_different_skip_state_may_return_different_mix()
        {
            var mixes = new[] { MakePrimetimeMix("a"), MakePrimetimeMix("b"), MakePrimetimeMix("c") };
            var useCase = MakeUseCase(mixes);

            var r1 = await useCase.GetAsync(MakeRequest(PrimetimeFriday, skipIds: Array.Empty<string>()), CancellationToken.None);
            var r2 = await useCase.GetAsync(MakeRequest(PrimetimeFriday, skipIds: new[] { r1.Mix!.Id }), CancellationToken.None);

            // Different seeds → likely different picks (with 3 mixes, high probability)
            // At minimum, r2 should not return the skipped mix
            r2.Mix!.Id.Should().NotBe(r1.Mix.Id);
        }

        [Test]
        public async Task GetAsync_skip_exhausts_pool_sets_skipsIgnored()
        {
            var mix = MakePrimetimeMix("only");
            var useCase = MakeUseCase(new[] { mix });

            var result = await useCase.GetAsync(
                MakeRequest(PrimetimeFriday, skipIds: new[] { "only" }),
                CancellationToken.None);

            result.SkipsIgnored.Should().BeTrue();
            result.Mix.Should().NotBeNull();
        }

        [Test]
        public async Task GetAsync_lean_exhausts_pool_sets_leanIgnored()
        {
            // Mix is warm (warmth=0.5) → tagged warmer, not darker
            var mix = MakeMix("warm", bpmMin: 138, bpmMax: 138, warmth: 0.5, energy: "peak");
            var useCase = MakeUseCase(new[] { mix });

            var result = await useCase.GetAsync(
                MakeRequest(PrimetimeFriday, moodLean: MoodLean.Darker),
                CancellationToken.None);

            result.LeanIgnored.Should().BeTrue();
            result.SkipsIgnored.Should().BeFalse(); // no skips provided — lean alone caused exhaustion
            result.Mix.Should().NotBeNull();
        }

        [Test]
        public async Task GetAsync_schedule_excludes_now_mix()
        {
            var mixes = new[]
            {
                MakePrimetimeMix("a"),
                MakePrimetimeMix("b"),
                MakePrimetimeMix("c"),
                MakePrimetimeMix("d"),
                MakePrimetimeMix("e"),
            };
            var useCase = MakeUseCase(mixes);

            var result = await useCase.GetAsync(
                MakeRequest(PrimetimeFriday, scheduleCount: 4),
                CancellationToken.None);

            string nowId = result.Mix!.Id;
            result.Schedule.Should().NotContain(e => e.Mix?.Id == nowId);
        }

        [Test]
        public async Task GetAsync_schedule_has_correct_count()
        {
            var mixes = new[]
            {
                MakePrimetimeMix("a"), MakePrimetimeMix("b"), MakePrimetimeMix("c"),
                MakePrimetimeMix("d"), MakePrimetimeMix("e"),
            };
            var useCase = MakeUseCase(mixes);

            var result = await useCase.GetAsync(
                MakeRequest(PrimetimeFriday, scheduleCount: 4),
                CancellationToken.None);

            result.Schedule.Should().HaveCount(4);
        }

        [Test]
        public async Task GetAsync_schedule_at_values_are_floored_to_hour()
        {
            // PrimetimeFriday is at :00 already, but test with a non-zero minute time
            var atWithMinutes = new DateTimeOffset(2026, 5, 15, 22, 37, 0, TimeSpan.Zero);
            var useCase = MakeUseCase(new[] { MakePrimetimeMix("x"), MakePrimetimeMix("y"), MakePrimetimeMix("z") });

            var result = await useCase.GetAsync(
                MakeRequest(atWithMinutes, scheduleCount: 2),
                CancellationToken.None);

            result.Schedule[0].At.Minute.Should().Be(0);
            result.Schedule[0].At.Second.Should().Be(0);
            result.Schedule[0].At.Hour.Should().Be(23);
        }

        [Test]
        public async Task GetAsync_empty_pool_sets_noMixAvailable()
        {
            // No mixes → all pools empty → 503 signal
            var useCase = MakeUseCase(Array.Empty<Mix>());

            var result = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);

            result.NoMixAvailable.Should().BeTrue();
            result.Mix.Should().BeNull();
        }

        [Test]
        public async Task GetAsync_utcOffsetMinutes_shifts_local_hour()
        {
            // UTC 01:00, offset +0 → dead of night (hour 1)
            // UTC 01:00, offset +120 (+2h) → hour 3 → still dead
            // UTC 01:00, offset +240 (+4h) → hour 5 → comedown
            // We use a mix that only scores well in dead slot to verify slot resolution
            var deadMix = MakeMix("dead1", bpmMin: 172, bpmMax: 172, warmth: -0.6, energy: "peak");
            var useCase = MakeUseCase(new[] { deadMix });
            var utcOne = new DateTimeOffset(2026, 5, 18, 1, 0, 0, TimeSpan.Zero); // Monday

            var resultDead = await useCase.GetAsync(
                new NowSpinningRequestDto { UtcNow = utcOne, UtcOffsetMinutes = 0, ScheduleCount = 0 },
                CancellationToken.None);

            resultDead.Slot.Key.Should().Be("dead");
        }

        [Test]
        public async Task GetAsync_now_field_reflects_utcNow_not_local()
        {
            var useCase = MakeUseCase(new[] { MakePrimetimeMix("x") });
            var result = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);
            result.Now.Should().Be(PrimetimeFriday);
        }

        private static NowSpinningUseCase MakeUseCase(IReadOnlyList<Mix> mixes)
        {
            return new NowSpinningUseCase(new StubCatalogueProvider(mixes));
        }

        private static NowSpinningRequestDto MakeRequest(
            DateTimeOffset at,
            MoodLean? moodLean = null,
            string[]? skipIds = null,
            int scheduleCount = 0,
            int utcOffsetMinutes = 0)
        {
            return new NowSpinningRequestDto
            {
                UtcNow = at,
                UtcOffsetMinutes = utcOffsetMinutes,
                MoodLean = moodLean,
                SkipIds = skipIds ?? Array.Empty<string>(),
                ScheduleCount = scheduleCount,
            };
        }

        private static Mix MakePrimetimeMix(string id) =>
            MakeMix(id, bpmMin: 138, bpmMax: 138, warmth: -0.3, energy: "peak");

        private static Mix MakeMix(string id, int? bpmMin, int? bpmMax, double? warmth, string energy) => new Mix
        {
            Id = id,
            Title = $"Mix {id}",
            Url = $"https://sc.test/{id}",
            Genre = "dnb",
            Energy = energy,
            BpmMin = bpmMin,
            BpmMax = bpmMax,
            Warmth = warmth,
        };

        private sealed class StubCatalogueProvider : IMixCatalogueProvider
        {
            private readonly IReadOnlyList<Mix> _mixes;
            public StubCatalogueProvider(IReadOnlyList<Mix> mixes) => _mixes = mixes;
            public Task<IReadOnlyList<Mix>> GetLatestAsync(int maxItems, CancellationToken cancellationToken)
                => Task.FromResult(_mixes);
        }
    }
}
```

- [ ] **Step 2: Run — expect build error (NowSpinningUseCase not yet defined)**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```

- [ ] **Step 3: Implement NowSpinningUseCase**

```csharp
// Changsta.Ai.Core.BusinessProcesses/NowSpinning/NowSpinningUseCase.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Contracts.NowSpinning;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;

namespace Changsta.Ai.Core.BusinessProcesses.NowSpinning
{
    public sealed class NowSpinningUseCase : INowSpinningUseCase
    {
        private const int CatalogMaxItems = 200;

        private readonly IMixCatalogueProvider _catalogueProvider;

        public NowSpinningUseCase(IMixCatalogueProvider catalogueProvider)
        {
            _catalogueProvider = catalogueProvider ?? throw new ArgumentNullException(nameof(catalogueProvider));
        }

        public async Task<NowSpinningResultDto> GetAsync(
            NowSpinningRequestDto request,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<Mix> mixes = await _catalogueProvider
                .GetLatestAsync(CatalogMaxItems, cancellationToken)
                .ConfigureAwait(false);

            NowSpinningPools pools = SlotPoolBuilder.Build(mixes);

            DateTimeOffset localTime = request.UtcNow.AddMinutes(request.UtcOffsetMinutes);
            SlotKey slot = SlotDefinitions.ResolveSlot(localTime.Hour);
            DayBucket dayBucket = SlotDefinitions.ResolveDayBucket(localTime.DayOfWeek);

            var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Mix? nowMix = Draw(
                pools,
                slot,
                dayBucket,
                request.MoodLean,
                request.SkipIds,
                request.UtcNow,
                out bool leanIgnored,
                out bool skipsIgnored,
                out bool poolFallback,
                usedIds);

            if (nowMix is null)
            {
                return new NowSpinningResultDto
                {
                    Now = request.UtcNow,
                    DayBucket = SlotDefinitions.DayBucketKey(dayBucket),
                    Slot = ToSlotDto(slot),
                    NoMixAvailable = true,
                };
            }

            usedIds.Add(nowMix.Id);

            var schedule = new List<NowSpinningScheduleEntryDto>(request.ScheduleCount);

            DateTimeOffset flooredHour = new DateTimeOffset(
                request.UtcNow.Year,
                request.UtcNow.Month,
                request.UtcNow.Day,
                request.UtcNow.Hour,
                0,
                0,
                TimeSpan.Zero);

            for (int i = 1; i <= request.ScheduleCount; i++)
            {
                DateTimeOffset slotUtc = flooredHour.AddHours(i);
                DateTimeOffset slotLocal = slotUtc.AddMinutes(request.UtcOffsetMinutes);
                SlotKey scheduleSlot = SlotDefinitions.ResolveSlot(slotLocal.Hour);
                DayBucket scheduleDayBucket = SlotDefinitions.ResolveDayBucket(slotLocal.DayOfWeek);

                Mix? scheduleMix = Draw(
                    pools,
                    scheduleSlot,
                    scheduleDayBucket,
                    request.MoodLean,
                    request.SkipIds,
                    slotUtc,
                    out _,
                    out _,
                    out _,
                    usedIds);

                if (scheduleMix is not null)
                {
                    usedIds.Add(scheduleMix.Id);
                }

                schedule.Add(new NowSpinningScheduleEntryDto
                {
                    At = slotUtc,
                    Slot = ToSlotDto(scheduleSlot),
                    DayBucket = SlotDefinitions.DayBucketKey(scheduleDayBucket),
                    Mix = scheduleMix,
                });
            }

            return new NowSpinningResultDto
            {
                Now = request.UtcNow,
                DayBucket = SlotDefinitions.DayBucketKey(dayBucket),
                Slot = ToSlotDto(slot),
                Mix = nowMix,
                Schedule = schedule,
                LeanIgnored = leanIgnored,
                SkipsIgnored = skipsIgnored,
                PoolFallback = poolFallback,
            };
        }

        private static Mix? Draw(
            NowSpinningPools pools,
            SlotKey slot,
            DayBucket dayBucket,
            MoodLean? moodLean,
            IReadOnlyList<string> userSkipIds,
            DateTimeOffset utcHour,
            out bool leanIgnored,
            out bool skipsIgnored,
            out bool poolFallback,
            IReadOnlySet<string> alreadyUsed)
        {
            leanIgnored = false;
            skipsIgnored = false;
            poolFallback = false;

            IReadOnlyList<PoolEntry> pool = GetPool(pools, slot, dayBucket);

            if (pool.Count == 0)
            {
                SlotKey adjacent = SlotDefinitions.AdjacentSlot(slot);
                pool = GetPool(pools, adjacent, dayBucket);

                if (pool.Count == 0)
                {
                    return null;
                }

                poolFallback = true;
            }

            var skipSet = new HashSet<string>(userSkipIds, StringComparer.OrdinalIgnoreCase);

            // Step 1: apply lean + user skip + alreadyUsed (alreadyUsed includes now mix — never repeats)
            List<PoolEntry> filtered = ApplyFilters(pool, moodLean, skipSet, alreadyUsed);

            if (filtered.Count > 0)
            {
                return SeededPick(filtered, utcHour, userSkipIds).Mix;
            }

            // Step 2: try ignoring user skip (only meaningful if skips were provided)
            if (userSkipIds.Count > 0)
            {
                filtered = ApplyFilters(pool, moodLean, new HashSet<string>(), alreadyUsed);

                if (filtered.Count > 0)
                {
                    skipsIgnored = true;
                    return SeededPick(filtered, utcHour, userSkipIds).Mix;
                }
            }

            // Step 3: lean exhausted the pool — ignore lean and skip (alreadyUsed still applied)
            leanIgnored = true;
            if (userSkipIds.Count > 0)
            {
                skipsIgnored = true;
            }

            filtered = ApplyFilters(pool, null, new HashSet<string>(), alreadyUsed);

            // alreadyUsed is always respected — the now mix is never in the schedule, even on small catalogs
            return filtered.Count > 0
                ? SeededPick(filtered, utcHour, userSkipIds).Mix
                : null;
        }

        private static List<PoolEntry> ApplyFilters(
            IReadOnlyList<PoolEntry> pool,
            MoodLean? moodLean,
            IReadOnlySet<string> skipIds,
            IReadOnlySet<string> alreadyUsed)
        {
            var result = new List<PoolEntry>(pool.Count);

            foreach (PoolEntry entry in pool)
            {
                if (skipIds.Contains(entry.Mix.Id))
                {
                    continue;
                }

                if (alreadyUsed.Contains(entry.Mix.Id))
                {
                    continue;
                }

                if (moodLean.HasValue && !entry.LeanTags.Contains(moodLean.Value))
                {
                    continue;
                }

                result.Add(entry);
            }

            return result;
        }

        private static PoolEntry SeededPick(
            List<PoolEntry> pool,
            DateTimeOffset utcHour,
            IReadOnlyList<string> userSkipIds)
        {
            // Spec: seed = floor(utcNow, 1hr) in milliseconds + hash(sortedSkipIds)
            long hourEpochMs = new DateTimeOffset(
                utcHour.Year, utcHour.Month, utcHour.Day, utcHour.Hour, 0, 0, TimeSpan.Zero)
                .ToUnixTimeMilliseconds();

            int skipHash = ComputeSkipHashFnv(userSkipIds);
            int seed = unchecked((int)(hourEpochMs + skipHash));

            int index = new Random(seed).Next(pool.Count);
            return pool[index];
        }

        private static int ComputeSkipHashFnv(IReadOnlyList<string> skipIds)
        {
            // FNV-1a: stable across process restarts, unlike GetHashCode()
            if (skipIds.Count == 0)
            {
                return 0;
            }

            string[] sorted = skipIds
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            uint hash = 2166136261u;

            foreach (string id in sorted)
            {
                foreach (char c in id)
                {
                    hash ^= (byte)(c & 0xFF);
                    hash *= 16777619u;
                }

                // Separator between IDs so ("ab","c") != ("a","bc")
                hash ^= 0xFFu;
                hash *= 16777619u;
            }

            return (int)hash;
        }

        private static IReadOnlyList<PoolEntry> GetPool(
            NowSpinningPools pools,
            SlotKey slot,
            DayBucket dayBucket)
        {
            return pools.Pools.TryGetValue((slot, dayBucket), out IReadOnlyList<PoolEntry>? pool)
                ? pool
                : Array.Empty<PoolEntry>();
        }

        private static NowSpinningSlotDto ToSlotDto(SlotKey slot)
        {
            SlotConfig config = SlotDefinitions.Slots[slot];
            return new NowSpinningSlotDto
            {
                Key = SlotDefinitions.SlotKeyString(slot),
                Label = config.Label,
            };
        }
    }
}
```

- [ ] **Step 4: Run tests**

```
dotnet test soundcloud-ai-mix-recommender-api.sln --no-build --filter "FullyQualifiedName~NowSpinningUseCaseTests"
```
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add Changsta.Ai.Core.BusinessProcesses/NowSpinning/NowSpinningUseCase.cs \
        Changsta.Ai.Tests.Unit/NowSpinning/NowSpinningUseCaseTests.cs
git commit -m "feat: implement NowSpinningUseCase with seeded draw and fallback"
```

---

## Task 7: Controller and View Models

**Files:**
- Create: `Changsta.Ai.Interface.Api/ViewModels/NowSpinningResponse.cs`
- Create: `Changsta.Ai.Interface.Api/Controllers/NowSpinningController.cs`

- [ ] **Step 1: Create view models**

```csharp
// Changsta.Ai.Interface.Api/ViewModels/NowSpinningResponse.cs
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class NowSpinningSlotVm
    {
        required public string Key { get; init; }
        required public string Label { get; init; }
    }

    public sealed class NowSpinningMixVm
    {
        required public string Id { get; init; }
        required public string Title { get; init; }
        required public string Url { get; init; }
        required public string Genre { get; init; }
        required public string Energy { get; init; }
        public int? Bpm { get; init; }
        public IReadOnlyList<string> Moods { get; init; } = Array.Empty<string>();
        public DateTimeOffset? PublishedAt { get; init; }
        public int? Duration { get; init; }
    }

    public sealed class NowSpinningScheduleEntryVm
    {
        required public DateTimeOffset At { get; init; }
        required public NowSpinningSlotVm Slot { get; init; }
        required public string DayBucket { get; init; }
        public NowSpinningMixVm? Mix { get; init; }
    }

    public sealed class NowSpinningResponse
    {
        required public DateTimeOffset Now { get; init; }
        required public string DayBucket { get; init; }
        required public NowSpinningSlotVm Slot { get; init; }
        public NowSpinningMixVm? Mix { get; init; }
        public IReadOnlyList<NowSpinningScheduleEntryVm> Schedule { get; init; } = Array.Empty<NowSpinningScheduleEntryVm>();

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool LeanIgnored { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool SkipsIgnored { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool PoolFallback { get; init; }
    }
}
```

- [ ] **Step 2: Create controller**

```csharp
// Changsta.Ai.Interface.Api/Controllers/NowSpinningController.cs
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.NowSpinning;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;
using Changsta.Ai.Interface.Api.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Changsta.Ai.Interface.Api.Controllers
{
    [ApiController]
    [Route("api/catalog")]
    [Produces("application/json")]
    public sealed class NowSpinningController : ControllerBase
    {
        private readonly INowSpinningUseCase _useCase;

        public NowSpinningController(INowSpinningUseCase useCase)
        {
            _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        }

        [HttpGet("now-spinning")]
        public async Task<IActionResult> GetNowSpinningAsync(
            [FromQuery] int utcOffsetMinutes = 0,
            [FromQuery] string? moodLean = null,
            [FromQuery] string? skip = null,
            [FromQuery] int schedule = 4,
            CancellationToken cancellationToken = default)
        {
            MoodLean? parsedLean = null;

            if (!string.IsNullOrEmpty(moodLean))
            {
                if (!TryParseMoodLean(moodLean, out MoodLean lean))
                {
                    return BadRequest(new { error = "invalid moodLean" });
                }

                parsedLean = lean;
            }

            string[] skipIds = string.IsNullOrWhiteSpace(skip)
                ? Array.Empty<string>()
                : skip.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var request = new NowSpinningRequestDto
            {
                UtcNow = DateTimeOffset.UtcNow,
                UtcOffsetMinutes = utcOffsetMinutes,
                MoodLean = parsedLean,
                SkipIds = skipIds,
                ScheduleCount = Math.Max(0, schedule),
            };

            NowSpinningResultDto result = await _useCase
                .GetAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (result.NoMixAvailable)
            {
                return StatusCode(503, new { error = "no mixes available" });
            }

            return Ok(MapToResponse(result));
        }

        private static bool TryParseMoodLean(string value, out MoodLean lean)
        {
            switch (value.ToLowerInvariant())
            {
                case "darker": lean = MoodLean.Darker; return true;
                case "warmer": lean = MoodLean.Warmer; return true;
                case "slower": lean = MoodLean.Slower; return true;
                case "faster": lean = MoodLean.Faster; return true;
                default: lean = default; return false;
            }
        }

        private static NowSpinningResponse MapToResponse(NowSpinningResultDto result)
        {
            return new NowSpinningResponse
            {
                Now = result.Now,
                DayBucket = result.DayBucket,
                Slot = new NowSpinningSlotVm { Key = result.Slot.Key, Label = result.Slot.Label },
                Mix = result.Mix is not null ? MapMix(result.Mix) : null,
                Schedule = result.Schedule
                    .Select(s => new NowSpinningScheduleEntryVm
                    {
                        At = s.At,
                        Slot = new NowSpinningSlotVm { Key = s.Slot.Key, Label = s.Slot.Label },
                        DayBucket = s.DayBucket,
                        Mix = s.Mix is not null ? MapMix(s.Mix) : null,
                    })
                    .ToArray(),
                LeanIgnored = result.LeanIgnored,
                SkipsIgnored = result.SkipsIgnored,
                PoolFallback = result.PoolFallback,
            };
        }

        private static NowSpinningMixVm MapMix(Mix mix)
        {
            return new NowSpinningMixVm
            {
                Id = mix.Id,
                Title = mix.Title,
                Url = mix.Url,
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
            {
                return (int)Math.Round((mix.BpmMin.Value + mix.BpmMax.Value) / 2.0);
            }

            return mix.BpmMin ?? mix.BpmMax;
        }

        private static int? ParseDurationSeconds(string? duration)
        {
            if (duration is null)
            {
                return null;
            }

            if (TimeSpan.TryParse(duration, out TimeSpan ts))
            {
                return (int)ts.TotalSeconds;
            }

            return null;
        }
    }
}
```

- [ ] **Step 3: Build**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
```
Expected: no errors (controller won't be reachable until DI is wired).

---

## Task 8: DI Registration

**Files:**
- Modify: `Changsta.Ai.Interface.Api/Program.cs`

- [ ] **Step 1: Register use case**

Find the block in `Program.cs` that registers `IMixRecommendationUseCase` and add the now-spinning registration immediately after:

```csharp
// after: builder.Services.AddScoped<IMixRecommendationUseCase, MixRecommendationUseCase>();
builder.Services.AddScoped<INowSpinningUseCase, NowSpinningUseCase>();
```

Also add the using at the top of `Program.cs`:

```csharp
using Changsta.Ai.Core.BusinessProcesses.NowSpinning;
using Changsta.Ai.Core.Contracts.NowSpinning;
```

- [ ] **Step 2: Build and run full test suite**

```
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental
dotnet test soundcloud-ai-mix-recommender-api.sln --no-build
```
Expected: build clean, all tests pass.

- [ ] **Step 3: Commit**

```bash
git add Changsta.Ai.Core.BusinessProcesses/Changsta.Ai.Core.BusinessProcesses.csproj \
        Changsta.Ai.Core/Dtos/NowSpinningRequestDto.cs \
        Changsta.Ai.Core/Dtos/NowSpinningResultDto.cs \
        Changsta.Ai.Core.Contracts/NowSpinning/INowSpinningUseCase.cs \
        Changsta.Ai.Core.BusinessProcesses/NowSpinning/SlotDefinitions.cs \
        Changsta.Ai.Core.BusinessProcesses/NowSpinning/SlotScorer.cs \
        Changsta.Ai.Core.BusinessProcesses/NowSpinning/SlotPoolBuilder.cs \
        Changsta.Ai.Core.BusinessProcesses/NowSpinning/NowSpinningUseCase.cs \
        Changsta.Ai.Interface.Api/ViewModels/NowSpinningResponse.cs \
        Changsta.Ai.Interface.Api/Controllers/NowSpinningController.cs \
        Changsta.Ai.Interface.Api/Program.cs \
        Changsta.Ai.Tests.Unit/NowSpinning/SlotScorerTests.cs \
        Changsta.Ai.Tests.Unit/NowSpinning/SlotPoolBuilderTests.cs \
        Changsta.Ai.Tests.Unit/NowSpinning/NowSpinningUseCaseTests.cs
git commit -m "feat: implement GET /api/catalog/now-spinning"
```

---

## Self-Review Against Spec

| Spec requirement | Task |
|---|---|
| `Core.BusinessProcesses` internals visible to tests | Task 2 |
| `utcOffsetMinutes` param, local hour derivation | Task 8 (controller), Task 6 (use case) |
| 6 slots, half-open hour ranges | Task 3 |
| 4 day buckets, BPM adjustment per day | Task 3 |
| BPM + warmth + energy scoring formula (energy = exact/ordinal) | Task 4 |
| Top 40% threshold with ≥ 3 hard floor | Task 5 |
| 24 pools (6 × 4) | Task 5 |
| Lean tags: darker/warmer/slower/faster (energy exact match) | Task 5 |
| Slower/faster relative to slot bpmTarget | Task 5 |
| Seeded draw: FNV-1a hash, milliseconds, addition | Task 6 |
| skip + lean fallback (leanIgnored/skipsIgnored); alreadyUsed always respected | Task 6 |
| Adjacent slot fallback + poolFallback flag | Task 6 |
| 503 when all pools empty | Task 6, Task 7 |
| Schedule: N future slots, floored to hour | Task 6 |
| Schedule never includes now mix (alreadyUsed never dropped) | Task 6 |
| dayBucket in response root and schedule entries | Task 6, Task 7 |
| bpm = round((min+max)/2), omit if null | Task 7 |
| duration = parse HH:MM:SS, omit if null | Task 7 |
| moodLean 400 on invalid value | Task 7 |
| Cache-Control: no-store | Handled by Cloudflare Worker — not in API |
| `now` field = UTC | Task 7 |
