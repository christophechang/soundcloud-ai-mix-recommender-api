using System;
using System.Collections.Generic;

namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class NowSpinningProgramResponse
    {
        required public DateTimeOffset Now { get; init; }

        required public string DayBucket { get; init; }

        required public NowSpinningSlotVm Slot { get; init; }

        required public IReadOnlyDictionary<string, NowSpinningProgramLaneVm> Lanes { get; init; }
    }
}
