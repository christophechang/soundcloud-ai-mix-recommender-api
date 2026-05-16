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

            // Step 1: all filters active
            List<PoolEntry> filtered = ApplyFilters(pool, moodLean, skipSet, alreadyUsed);

            if (filtered.Count > 0)
            {
                return SeededPick(filtered, utcHour, userSkipIds, moodLean).Mix;
            }

            // Step 2: try ignoring user skip (only if skips were actually provided)
            if (userSkipIds.Count > 0)
            {
                filtered = ApplyFilters(pool, moodLean, new HashSet<string>(), alreadyUsed);

                if (filtered.Count > 0)
                {
                    skipsIgnored = true;
                    return SeededPick(filtered, utcHour, userSkipIds, moodLean).Mix;
                }
            }

            // Step 3: lean exhausted pool — ignore lean and skip
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
