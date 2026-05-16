using System;
using System.Collections.Generic;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class NowSpinningResultDto
    {
        required public DateTimeOffset Now { get; init; }

        required public string DayBucket { get; init; }

        required public NowSpinningSlotDto Slot { get; init; }

        public Mix? Mix { get; init; }

        public IReadOnlyList<NowSpinningScheduleEntryDto> Schedule { get; init; } = Array.Empty<NowSpinningScheduleEntryDto>();

        public bool LeanIgnored { get; init; }

        public bool PoolFallback { get; init; }

        public bool NoMixAvailable { get; init; }
    }
}
