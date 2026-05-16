using System;
using System.Collections.Generic;
using System.Linq;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;

namespace Changsta.Ai.Core.BusinessProcesses.NowSpinning
{
    internal static class NowSpinningDrawer
    {
        internal static Mix? Draw(
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

        internal static NowSpinningSlotDto ToSlotDto(SlotKey slot)
        {
            SlotConfig config = SlotDefinitions.Slots[slot];
            return new NowSpinningSlotDto
            {
                Key = SlotDefinitions.SlotKeyString(slot),
                Label = config.Label,
            };
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
    }
}
