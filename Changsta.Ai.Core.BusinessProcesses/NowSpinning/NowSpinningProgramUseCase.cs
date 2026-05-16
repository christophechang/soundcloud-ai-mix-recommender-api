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
                request.UtcNow.Year,
                request.UtcNow.Month,
                request.UtcNow.Day,
                request.UtcNow.Hour,
                0,
                0,
                TimeSpan.Zero)
                .ToUnixTimeMilliseconds();

            int[] shuffledIndexes = ShuffleLaneIndexes(hourEpochMs);

            var crossLaneUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nowMixes = new Mix?[LaneOrder.Length];
            var leanIgnoredFlags = new bool[LaneOrder.Length];
            var skipsIgnoredFlags = new bool[LaneOrder.Length];
            var poolFallbackFlags = new bool[LaneOrder.Length];

            foreach (int laneIndex in shuffledIndexes)
            {
                Mix? nowMix = Draw(
                    pools,
                    slot,
                    dayBucket,
                    LaneOrder[laneIndex],
                    request.SkipIds,
                    request.UtcNow,
                    out bool leanIgnored,
                    out bool skipsIgnored,
                    out bool poolFallback,
                    crossLaneUsed);

                nowMixes[laneIndex] = nowMix;
                leanIgnoredFlags[laneIndex] = leanIgnored;
                skipsIgnoredFlags[laneIndex] = skipsIgnored;
                poolFallbackFlags[laneIndex] = poolFallback;

                if (nowMix is not null)
                {
                    crossLaneUsed.Add(nowMix.Id);
                }
            }

            DateTimeOffset flooredHour = new DateTimeOffset(
                request.UtcNow.Year,
                request.UtcNow.Month,
                request.UtcNow.Day,
                request.UtcNow.Hour,
                0,
                0,
                TimeSpan.Zero);

            var lanes = new Dictionary<string, NowSpinningProgramLaneDto>(LaneKeys.Length);

            for (int i = 0; i < LaneOrder.Length; i++)
            {
                MoodLean? lean = LaneOrder[i];
                string key = LaneKeys[i];

                Mix? nowMix = nowMixes[i];
                bool leanIgnored = leanIgnoredFlags[i];
                bool skipsIgnored = skipsIgnoredFlags[i];
                bool poolFallback = poolFallbackFlags[i];

                var laneUsed = new HashSet<string>(crossLaneUsed, StringComparer.OrdinalIgnoreCase);

                var schedule = new List<NowSpinningScheduleEntryDto>(request.ScheduleCount);

                for (int s = 1; s <= request.ScheduleCount; s++)
                {
                    DateTimeOffset slotUtc = flooredHour.AddHours(s);
                    DateTimeOffset slotLocal = slotUtc.AddMinutes(request.UtcOffsetMinutes);
                    SlotKey scheduleSlot = SlotDefinitions.ResolveSlot(slotLocal.Hour);
                    DayBucket scheduleDayBucket = SlotDefinitions.ResolveDayBucket(slotLocal.DayOfWeek);

                    Mix? scheduleMix = Draw(
                        pools,
                        scheduleSlot,
                        scheduleDayBucket,
                        lean,
                        request.SkipIds,
                        slotUtc,
                        out _,
                        out _,
                        out _,
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

        private static int[] ShuffleLaneIndexes(long hourEpochMs)
        {
            var indexes = new int[LaneOrder.Length];
            for (int i = 0; i < indexes.Length; i++)
            {
                indexes[i] = i;
            }

            var rng = new Random(unchecked((int)hourEpochMs));

            for (int i = indexes.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (indexes[i], indexes[j]) = (indexes[j], indexes[i]);
            }

            return indexes;
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
                utcHour.Year,
                utcHour.Month,
                utcHour.Day,
                utcHour.Hour,
                0,
                0,
                TimeSpan.Zero)
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
