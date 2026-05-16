using System;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class NowSpinningScheduleEntryDto
    {
        required public DateTimeOffset At { get; init; }

        required public NowSpinningSlotDto Slot { get; init; }

        required public string DayBucket { get; init; }

        public Mix? Mix { get; init; }
    }
}
