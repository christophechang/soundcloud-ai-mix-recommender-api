using System;
using System.Collections.Generic;
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
            DateTimeOffset utcHour,
            out bool leanIgnored,
            out bool poolFallback,
            IReadOnlySet<string> alreadyUsed)
        {
            leanIgnored = false;
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

            List<PoolEntry> filtered = ApplyFilters(pool, moodLean, alreadyUsed);

            if (filtered.Count > 0)
            {
                return SeededPick(filtered, utcHour, moodLean).Mix;
            }

            leanIgnored = true;
            filtered = ApplyFilters(pool, null, alreadyUsed);

            return filtered.Count > 0
                ? SeededPick(filtered, utcHour, moodLean).Mix
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
            IReadOnlySet<string> alreadyUsed)
        {
            var result = new List<PoolEntry>(pool.Count);

            foreach (PoolEntry entry in pool)
            {
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

            int moodHash = moodLean.HasValue ? ((int)moodLean.Value + 1) * 397 : 0;
            int seed = unchecked((int)hourEpochMs ^ moodHash);

            int index = new Random(seed).Next(pool.Count);
            return pool[index];
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
