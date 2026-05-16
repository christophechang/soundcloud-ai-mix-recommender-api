using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class NowSpinningProgramResultDto
    {
        required public DateTimeOffset Now { get; init; }

        required public string DayBucket { get; init; }

        required public NowSpinningSlotDto Slot { get; init; }

        required public IReadOnlyDictionary<string, NowSpinningProgramLaneDto> Lanes { get; init; }
    }
}
