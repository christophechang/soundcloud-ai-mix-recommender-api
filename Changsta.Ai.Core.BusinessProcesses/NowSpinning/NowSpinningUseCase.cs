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

            Mix? nowMix = NowSpinningDrawer.Draw(
                pools,
                slot,
                dayBucket,
                request.MoodLean,
                request.UtcNow,
                out bool leanIgnored,
                out bool poolFallback,
                usedIds);

            if (nowMix is null)
            {
                return new NowSpinningResultDto
                {
                    Now = request.UtcNow,
                    DayBucket = SlotDefinitions.DayBucketKey(dayBucket),
                    Slot = NowSpinningDrawer.ToSlotDto(slot),
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

                Mix? scheduleMix = NowSpinningDrawer.Draw(
                    pools,
                    scheduleSlot,
                    scheduleDayBucket,
                    request.MoodLean,
                    slotUtc,
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
                    Slot = NowSpinningDrawer.ToSlotDto(scheduleSlot),
                    DayBucket = SlotDefinitions.DayBucketKey(scheduleDayBucket),
                    Mix = scheduleMix,
                });
            }

            return new NowSpinningResultDto
            {
                Now = request.UtcNow,
                DayBucket = SlotDefinitions.DayBucketKey(dayBucket),
                Slot = NowSpinningDrawer.ToSlotDto(slot),
                Mix = nowMix,
                Schedule = schedule,
                LeanIgnored = leanIgnored,
                PoolFallback = poolFallback,
            };
        }
    }
}
