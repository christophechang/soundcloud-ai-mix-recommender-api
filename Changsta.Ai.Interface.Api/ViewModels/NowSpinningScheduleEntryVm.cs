using System;

namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class NowSpinningScheduleEntryVm
    {
        required public DateTimeOffset At { get; init; }

        required public NowSpinningSlotVm Slot { get; init; }

        required public string DayBucket { get; init; }

        public NowSpinningMixVm? Mix { get; init; }
    }
}
