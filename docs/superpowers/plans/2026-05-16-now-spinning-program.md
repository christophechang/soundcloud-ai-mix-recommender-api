# Now Spinning Program Endpoint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `GET /api/catalog/now-spinning/program` that computes picks for all 5 mood lanes (default, darker, warmer, slower, faster) in a single call, guaranteeing the "now playing" mix is unique across all lanes.

**Architecture:** A new use case draws all 5 lanes sequentially using a shared `alreadyUsed` set — each lane's now-pick is reserved before the next lane draws. Draw order is shuffled deterministically per UTC hour so no lane is permanently disadvantaged. Results are cached in-memory keyed by `(utcHourEpoch, utcOffsetMinutes)` and expire at the next UTC hour boundary. Existing `GET /api/catalog/now-spinning` is unchanged.

**Tech Stack:** .NET 10, ASP.NET Core, NUnit, FluentAssertions, `Microsoft.Extensions.Caching.Memory` (already registered)

---

## File Map

**Create:**
- `Changsta.Ai.Core/Dtos/NowSpinningProgramRequestDto.cs` — request (no MoodLean)
- `Changsta.Ai.Core/Dtos/NowSpinningProgramLaneDto.cs` — per-lane result
- `Changsta.Ai.Core/Dtos/NowSpinningProgramResultDto.cs` — top-level result with Lanes dict
- `Changsta.Ai.Core.Contracts/NowSpinning/INowSpinningProgramUseCase.cs` — interface
- `Changsta.Ai.Core.BusinessProcesses/NowSpinning/NowSpinningProgramUseCase.cs` — implementation
- `Changsta.Ai.Interface.Api/ViewModels/NowSpinningProgramLaneVm.cs` — lane VM
- `Changsta.Ai.Interface.Api/ViewModels/NowSpinningProgramResponse.cs` — response VM
- `Changsta.Ai.Interface.Api/Controllers/NowSpinningProgramController.cs` — controller
- `Changsta.Ai.Tests.Unit/NowSpinning/NowSpinningProgramUseCaseTests.cs` — tests

**Modify:**
- `Changsta.Ai.Interface.Api/Program.cs:175` — add DI registration for `INowSpinningProgramUseCase`

---

## Task 1: DTOs

**Files:**
- Create: `Changsta.Ai.Core/Dtos/NowSpinningProgramRequestDto.cs`
- Create: `Changsta.Ai.Core/Dtos/NowSpinningProgramLaneDto.cs`
- Create: `Changsta.Ai.Core/Dtos/NowSpinningProgramResultDto.cs`

- [ ] **Step 1: Create NowSpinningProgramRequestDto**

```csharp
using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class NowSpinningProgramRequestDto
    {
        required public DateTimeOffset UtcNow { get; init; }

        public int UtcOffsetMinutes { get; init; } = 0;

        public IReadOnlyList<string> SkipIds { get; init; } = Array.Empty<string>();

        public int ScheduleCount { get; init; } = 4;
    }
}
```

- [ ] **Step 2: Create NowSpinningProgramLaneDto**

```csharp
using System;
using System.Collections.Generic;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class NowSpinningProgramLaneDto
    {
        public Mix? Mix { get; init; }

        public IReadOnlyList<NowSpinningScheduleEntryDto> Schedule { get; init; } = Array.Empty<NowSpinningScheduleEntryDto>();

        public bool LeanIgnored { get; init; }

        public bool SkipsIgnored { get; init; }

        public bool PoolFallback { get; init; }

        public bool NoMixAvailable { get; init; }
    }
}
```

- [ ] **Step 3: Create NowSpinningProgramResultDto**

```csharp
using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class NowSpinningProgramResultDto
    {
        required public DateTimeOffset Now { get; init; }

        required public string DayBucket { get; init; }

        required public NowSpinningSlotDto Slot { get; init; }

        required public IReadOnlyDictionary<string, NowSpinningProgramLaneDto> Lanes { get; init; }
    }
}
```

- [ ] **Step 4: Build**

```bash
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental -q
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Changsta.Ai.Core/Dtos/NowSpinningProgramRequestDto.cs \
        Changsta.Ai.Core/Dtos/NowSpinningProgramLaneDto.cs \
        Changsta.Ai.Core/Dtos/NowSpinningProgramResultDto.cs
git commit -m "feat: add NowSpinningProgram DTOs"
```

---

## Task 2: Contract interface

**Files:**
- Create: `Changsta.Ai.Core.Contracts/NowSpinning/INowSpinningProgramUseCase.cs`

- [ ] **Step 1: Create interface**

```csharp
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Dtos;

namespace Changsta.Ai.Core.Contracts.NowSpinning
{
    public interface INowSpinningProgramUseCase
    {
        Task<NowSpinningProgramResultDto> GetAsync(NowSpinningProgramRequestDto request, CancellationToken cancellationToken);
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental -q
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Changsta.Ai.Core.Contracts/NowSpinning/INowSpinningProgramUseCase.cs
git commit -m "feat: add INowSpinningProgramUseCase contract"
```

---

## Task 3: Use case — write failing tests first

**Files:**
- Create: `Changsta.Ai.Tests.Unit/NowSpinning/NowSpinningProgramUseCaseTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
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
    public sealed class NowSpinningProgramUseCaseTests
    {
        // 2026-05-15 is a Friday, UTC 22:00 → primetime
        private static readonly DateTimeOffset PrimetimeFriday =
            new DateTimeOffset(2026, 5, 15, 22, 0, 0, TimeSpan.Zero);

        [Test]
        public async Task GetAsync_all_five_lanes_present()
        {
            var mixes = MakePrimetimeMixes("a", "b", "c", "d", "e", "f");
            var useCase = MakeUseCase(mixes);

            var result = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);

            result.Lanes.Keys.Should().BeEquivalentTo(new[] { "default", "darker", "warmer", "slower", "faster" });
        }

        [Test]
        public async Task GetAsync_now_picks_are_unique_across_lanes()
        {
            var mixes = MakePrimetimeMixes("a", "b", "c", "d", "e", "f", "g", "h");
            var useCase = MakeUseCase(mixes);

            var result = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);

            var nowIds = result.Lanes.Values
                .Where(l => l.Mix is not null)
                .Select(l => l.Mix!.Id)
                .ToList();

            nowIds.Should().OnlyHaveUniqueItems();
        }

        [Test]
        public async Task GetAsync_same_request_returns_same_result()
        {
            var mixes = MakePrimetimeMixes("a", "b", "c", "d", "e", "f");
            var useCase = MakeUseCase(mixes);

            var r1 = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);
            var r2 = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);

            r1.Lanes["default"].Mix!.Id.Should().Be(r2.Lanes["default"].Mix!.Id);
            r1.Lanes["darker"].Mix!.Id.Should().Be(r2.Lanes["darker"].Mix!.Id);
        }

        [Test]
        public async Task GetAsync_schedule_has_correct_count()
        {
            var mixes = MakePrimetimeMixes("a", "b", "c", "d", "e", "f", "g", "h", "i", "j");
            var useCase = MakeUseCase(mixes);

            var result = await useCase.GetAsync(MakeRequest(PrimetimeFriday, scheduleCount: 3), CancellationToken.None);

            result.Lanes["default"].Schedule.Should().HaveCount(3);
        }

        [Test]
        public async Task GetAsync_now_field_reflects_utcNow()
        {
            var mixes = MakePrimetimeMixes("a", "b", "c", "d", "e");
            var useCase = MakeUseCase(mixes);

            var result = await useCase.GetAsync(MakeRequest(PrimetimeFriday), CancellationToken.None);

            result.Now.Should().Be(PrimetimeFriday);
        }

        private static NowSpinningProgramUseCase MakeUseCase(IReadOnlyList<Mix> mixes)
            => new NowSpinningProgramUseCase(new StubCatalogueProvider(mixes));

        private static NowSpinningProgramRequestDto MakeRequest(
            DateTimeOffset at,
            int scheduleCount = 0,
            int utcOffsetMinutes = 0,
            string[]? skipIds = null)
        {
            return new NowSpinningProgramRequestDto
            {
                UtcNow = at,
                UtcOffsetMinutes = utcOffsetMinutes,
                SkipIds = skipIds ?? Array.Empty<string>(),
                ScheduleCount = scheduleCount,
            };
        }

        private static IReadOnlyList<Mix> MakePrimetimeMixes(params string[] ids)
            => ids.Select(id => MakeMix(id)).ToArray();

        private static Mix MakeMix(string id) => new Mix
        {
            Id = id,
            Title = $"Mix {id}",
            Url = $"https://sc.test/{id}",
            Genre = "dnb",
            Energy = "peak",
            BpmMin = 138,
            BpmMax = 138,
            Warmth = -0.3,
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

- [ ] **Step 2: Run tests — expect compile failure (type not found)**

```bash
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental -q 2>&1 | grep -i error
```

Expected: error — `NowSpinningProgramUseCase` does not exist.

---

## Task 4: Use case — implementation

**Files:**
- Create: `Changsta.Ai.Core.BusinessProcesses/NowSpinning/NowSpinningProgramUseCase.cs`

- [ ] **Step 1: Create the use case**

```csharp
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
    public sealed class NowSpinningProgramUseCase : INowSpinningProgramUseCase
    {
        private const int CatalogMaxItems = 200;

        private static readonly MoodLean?[] LaneOrder =
            new MoodLean?[] { null, MoodLean.Darker, MoodLean.Warmer, MoodLean.Slower, MoodLean.Faster };

        private static readonly string[] LaneKeys =
            new[] { "default", "darker", "warmer", "slower", "faster" };

        private readonly IMixCatalogueProvider _catalogueProvider;

        public NowSpinningProgramUseCase(IMixCatalogueProvider catalogueProvider)
        {
            _catalogueProvider = catalogueProvider ?? throw new ArgumentNullException(nameof(catalogueProvider));
        }

        public async Task<NowSpinningProgramResultDto> GetAsync(
            NowSpinningProgramRequestDto request,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<Mix> mixes = await _catalogueProvider
                .GetLatestAsync(CatalogMaxItems, cancellationToken)
                .ConfigureAwait(false);

            NowSpinningPools pools = SlotPoolBuilder.Build(mixes);

            DateTimeOffset localTime = request.UtcNow.AddMinutes(request.UtcOffsetMinutes);
            SlotKey slot = SlotDefinitions.ResolveSlot(localTime.Hour);
            DayBucket dayBucket = SlotDefinitions.ResolveDayBucket(localTime.DayOfWeek);

            long hourEpochMs = new DateTimeOffset(
                request.UtcNow.Year, request.UtcNow.Month, request.UtcNow.Day,
                request.UtcNow.Hour, 0, 0, TimeSpan.Zero)
                .ToUnixTimeMilliseconds();

            // Shuffle draw order per hour so no lane is permanently last
            MoodLean?[] shuffled = ShuffleLaneOrder(hourEpochMs);

            var crossLaneUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var laneResults = new Dictionary<MoodLean?, (Mix? nowMix, bool leanIgnored, bool skipsIgnored, bool poolFallback)>();

            // First pass: draw now-picks in shuffled order with cross-lane exclusion
            foreach (MoodLean? lean in shuffled)
            {
                Mix? nowMix = Draw(
                    pools, slot, dayBucket, lean,
                    request.SkipIds, request.UtcNow,
                    out bool leanIgnored, out bool skipsIgnored, out bool poolFallback,
                    crossLaneUsed);

                laneResults[lean] = (nowMix, leanIgnored, skipsIgnored, poolFallback);

                if (nowMix is not null)
                {
                    crossLaneUsed.Add(nowMix.Id);
                }
            }

            // Second pass: build schedules per lane (each starts with all now-picks reserved)
            var lanes = new Dictionary<string, NowSpinningProgramLaneDto>(LaneKeys.Length);

            DateTimeOffset flooredHour = new DateTimeOffset(
                request.UtcNow.Year, request.UtcNow.Month, request.UtcNow.Day,
                request.UtcNow.Hour, 0, 0, TimeSpan.Zero);

            for (int i = 0; i < LaneOrder.Length; i++)
            {
                MoodLean? lean = LaneOrder[i];
                string key = LaneKeys[i];

                var (nowMix, leanIgnored, skipsIgnored, poolFallback) = laneResults[lean];

                var laneUsed = new HashSet<string>(crossLaneUsed, StringComparer.OrdinalIgnoreCase);

                var schedule = new List<NowSpinningScheduleEntryDto>(request.ScheduleCount);

                for (int s = 1; s <= request.ScheduleCount; s++)
                {
                    DateTimeOffset slotUtc = flooredHour.AddHours(s);
                    DateTimeOffset slotLocal = slotUtc.AddMinutes(request.UtcOffsetMinutes);
                    SlotKey scheduleSlot = SlotDefinitions.ResolveSlot(slotLocal.Hour);
                    DayBucket scheduleDayBucket = SlotDefinitions.ResolveDayBucket(slotLocal.DayOfWeek);

                    Mix? scheduleMix = Draw(
                        pools, scheduleSlot, scheduleDayBucket, lean,
                        request.SkipIds, slotUtc,
                        out _, out _, out _,
                        laneUsed);

                    if (scheduleMix is not null)
                    {
                        laneUsed.Add(scheduleMix.Id);
                    }

                    schedule.Add(new NowSpinningScheduleEntryDto
                    {
                        At = slotUtc,
                        Slot = ToSlotDto(scheduleSlot),
                        DayBucket = SlotDefinitions.DayBucketKey(scheduleDayBucket),
                        Mix = scheduleMix,
                    });
                }

                lanes[key] = new NowSpinningProgramLaneDto
                {
                    Mix = nowMix,
                    Schedule = schedule,
                    LeanIgnored = leanIgnored,
                    SkipsIgnored = skipsIgnored,
                    PoolFallback = poolFallback,
                    NoMixAvailable = nowMix is null,
                };
            }

            return new NowSpinningProgramResultDto
            {
                Now = request.UtcNow,
                DayBucket = SlotDefinitions.DayBucketKey(dayBucket),
                Slot = ToSlotDto(slot),
                Lanes = lanes,
            };
        }

        private static MoodLean?[] ShuffleLaneOrder(long hourEpochMs)
        {
            var order = (MoodLean?[])LaneOrder.Clone();
            var rng = new Random(unchecked((int)hourEpochMs));

            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            return order;
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

            List<PoolEntry> filtered = ApplyFilters(pool, moodLean, skipSet, alreadyUsed);

            if (filtered.Count > 0)
            {
                return SeededPick(filtered, utcHour, userSkipIds, moodLean).Mix;
            }

            if (userSkipIds.Count > 0)
            {
                filtered = ApplyFilters(pool, moodLean, new HashSet<string>(), alreadyUsed);

                if (filtered.Count > 0)
                {
                    skipsIgnored = true;
                    return SeededPick(filtered, utcHour, userSkipIds, moodLean).Mix;
                }
            }

            leanIgnored = true;
            if (userSkipIds.Count > 0)
            {
                skipsIgnored = true;
            }

            filtered = ApplyFilters(pool, null, new HashSet<string>(), alreadyUsed);

            return filtered.Count > 0
                ? SeededPick(filtered, utcHour, userSkipIds, moodLean).Mix
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
            IReadOnlyList<string> userSkipIds,
            MoodLean? moodLean)
        {
            long hourEpochMs = new DateTimeOffset(
                utcHour.Year, utcHour.Month, utcHour.Day, utcHour.Hour, 0, 0, TimeSpan.Zero)
                .ToUnixTimeMilliseconds();

            int skipHash = ComputeSkipHashFnv(userSkipIds);
            int moodHash = moodLean.HasValue ? ((int)moodLean.Value + 1) * 397 : 0;
            int seed = unchecked((int)(hourEpochMs + skipHash) ^ moodHash);

            int index = new Random(seed).Next(pool.Count);
            return pool[index];
        }

        private static int ComputeSkipHashFnv(IReadOnlyList<string> skipIds)
        {
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

- [ ] **Step 2: Build**

```bash
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental -q
```

Expected: 0 errors.

- [ ] **Step 3: Run tests**

```bash
dotnet test soundcloud-ai-mix-recommender-api.sln --no-build --filter "NowSpinningProgram"
```

Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add Changsta.Ai.Core.BusinessProcesses/NowSpinning/NowSpinningProgramUseCase.cs \
        Changsta.Ai.Tests.Unit/NowSpinning/NowSpinningProgramUseCaseTests.cs
git commit -m "feat: implement NowSpinningProgramUseCase with cross-lane uniqueness"
```

---

## Task 5: View models

**Files:**
- Create: `Changsta.Ai.Interface.Api/ViewModels/NowSpinningProgramLaneVm.cs`
- Create: `Changsta.Ai.Interface.Api/ViewModels/NowSpinningProgramResponse.cs`

- [ ] **Step 1: Create NowSpinningProgramLaneVm**

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class NowSpinningProgramLaneVm
    {
        public NowSpinningMixVm? Mix { get; init; }

        public IReadOnlyList<NowSpinningScheduleEntryVm> Schedule { get; init; } = Array.Empty<NowSpinningScheduleEntryVm>();

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool LeanIgnored { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool SkipsIgnored { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool PoolFallback { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool NoMixAvailable { get; init; }
    }
}
```

- [ ] **Step 2: Create NowSpinningProgramResponse**

```csharp
using System;
using System.Collections.Generic;

namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class NowSpinningProgramResponse
    {
        required public DateTimeOffset Now { get; init; }

        required public string DayBucket { get; init; }

        required public NowSpinningSlotVm Slot { get; init; }

        required public IReadOnlyDictionary<string, NowSpinningProgramLaneVm> Lanes { get; init; }
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental -q
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Changsta.Ai.Interface.Api/ViewModels/NowSpinningProgramLaneVm.cs \
        Changsta.Ai.Interface.Api/ViewModels/NowSpinningProgramResponse.cs
git commit -m "feat: add NowSpinningProgram view models"
```

---

## Task 6: Controller + DI

**Files:**
- Create: `Changsta.Ai.Interface.Api/Controllers/NowSpinningProgramController.cs`
- Modify: `Changsta.Ai.Interface.Api/Program.cs`

- [ ] **Step 1: Create controller**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.NowSpinning;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;
using Changsta.Ai.Interface.Api.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Changsta.Ai.Interface.Api.Controllers
{
    [ApiController]
    [Route("api/catalog")]
    [Produces("application/json")]
    public sealed class NowSpinningProgramController : ControllerBase
    {
        private readonly INowSpinningProgramUseCase _useCase;
        private readonly IMemoryCache _cache;

        public NowSpinningProgramController(INowSpinningProgramUseCase useCase, IMemoryCache cache)
        {
            _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        [HttpGet("now-spinning/program")]
        public async Task<IActionResult> GetProgramAsync(
            [FromQuery] int utcOffsetMinutes = 0,
            [FromQuery] string? skip = null,
            [FromQuery] int schedule = 4,
            CancellationToken cancellationToken = default)
        {
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;

            long hourEpochMs = new DateTimeOffset(
                utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, 0, 0, TimeSpan.Zero)
                .ToUnixTimeMilliseconds();

            string cacheKey = $"now-spinning-program:{hourEpochMs}:{utcOffsetMinutes}";

            if (_cache.TryGetValue(cacheKey, out NowSpinningProgramResponse? cached))
            {
                return Ok(cached);
            }

            string[] skipIds = string.IsNullOrWhiteSpace(skip)
                ? Array.Empty<string>()
                : skip.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var request = new NowSpinningProgramRequestDto
            {
                UtcNow = utcNow,
                UtcOffsetMinutes = utcOffsetMinutes,
                SkipIds = skipIds,
                ScheduleCount = Math.Max(0, schedule),
            };

            NowSpinningProgramResultDto result = await _useCase
                .GetAsync(request, cancellationToken)
                .ConfigureAwait(false);

            NowSpinningProgramResponse response = MapToResponse(result);

            DateTimeOffset nextHour = new DateTimeOffset(
                utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, 0, 0, TimeSpan.Zero)
                .AddHours(1);

            _cache.Set(cacheKey, response, new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = nextHour,
            });

            return Ok(response);
        }

        private static NowSpinningProgramResponse MapToResponse(NowSpinningProgramResultDto result)
        {
            var lanes = new Dictionary<string, NowSpinningProgramLaneVm>(result.Lanes.Count);

            foreach (KeyValuePair<string, NowSpinningProgramLaneDto> kvp in result.Lanes)
            {
                lanes[kvp.Key] = MapLane(kvp.Value);
            }

            return new NowSpinningProgramResponse
            {
                Now = result.Now,
                DayBucket = result.DayBucket,
                Slot = new NowSpinningSlotVm { Key = result.Slot.Key, Label = result.Slot.Label },
                Lanes = lanes,
            };
        }

        private static NowSpinningProgramLaneVm MapLane(NowSpinningProgramLaneDto lane)
        {
            return new NowSpinningProgramLaneVm
            {
                Mix = lane.Mix is not null ? MapMix(lane.Mix) : null,
                Schedule = lane.Schedule
                    .Select(s => new NowSpinningScheduleEntryVm
                    {
                        At = s.At,
                        Slot = new NowSpinningSlotVm { Key = s.Slot.Key, Label = s.Slot.Label },
                        DayBucket = s.DayBucket,
                        Mix = s.Mix is not null ? MapMix(s.Mix) : null,
                    })
                    .ToArray(),
                LeanIgnored = lane.LeanIgnored,
                SkipsIgnored = lane.SkipsIgnored,
                PoolFallback = lane.PoolFallback,
                NoMixAvailable = lane.NoMixAvailable,
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

- [ ] **Step 2: Register in DI — add after line 175 in Program.cs**

Find this line in `Changsta.Ai.Interface.Api/Program.cs`:
```csharp
builder.Services.AddScoped<INowSpinningUseCase, NowSpinningUseCase>();
```

Add immediately after it:
```csharp
builder.Services.AddScoped<INowSpinningProgramUseCase, NowSpinningProgramUseCase>();
```

- [ ] **Step 3: Build**

```bash
dotnet build soundcloud-ai-mix-recommender-api.sln --no-incremental -q
```

Expected: 0 errors.

- [ ] **Step 4: Run full test suite**

```bash
dotnet test soundcloud-ai-mix-recommender-api.sln --no-build
```

Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add Changsta.Ai.Interface.Api/Controllers/NowSpinningProgramController.cs \
        Changsta.Ai.Interface.Api/ViewModels/NowSpinningProgramLaneVm.cs \
        Changsta.Ai.Interface.Api/ViewModels/NowSpinningProgramResponse.cs \
        Changsta.Ai.Interface.Api/Program.cs
git commit -m "feat: add GET /api/catalog/now-spinning/program endpoint"
```
