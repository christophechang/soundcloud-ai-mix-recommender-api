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
            int dayNumber = (int)(utcHour.ToUnixTimeSeconds() / 86400);
            int weekNumber = dayNumber / 7;
            int moodHash = moodLean.HasValue ? ((int)moodLean.Value + 1) * 397 : 0;
            int weekSeed = unchecked(weekNumber ^ moodHash);

            // Shuffle pool indices once per week so each day of the week maps to a
            // distinct entry — prevents the same mix being picked on multiple days.
            int[] indices = new int[pool.Count];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            var rng = new Random(weekSeed);
            for (int i = indices.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int tmp = indices[i];
                indices[i] = indices[j];
                indices[j] = tmp;
            }

            int position = (dayNumber % 7) % pool.Count;
            return pool[indices[position]];
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
