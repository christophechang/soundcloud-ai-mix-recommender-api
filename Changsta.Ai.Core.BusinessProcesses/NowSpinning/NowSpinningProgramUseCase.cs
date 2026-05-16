using System;
using System.Collections.Generic;
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
                Mix? nowMix = NowSpinningDrawer.Draw(
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

                    Mix? scheduleMix = NowSpinningDrawer.Draw(
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
                        Slot = NowSpinningDrawer.ToSlotDto(scheduleSlot),
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
                Slot = NowSpinningDrawer.ToSlotDto(slot),
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
    }
}
