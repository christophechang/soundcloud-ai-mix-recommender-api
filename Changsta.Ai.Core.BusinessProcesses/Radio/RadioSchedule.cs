using System.Collections.Generic;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    internal sealed class RadioSchedule
    {
        required internal System.DateOnly ScheduleDate { get; init; }

        required internal IReadOnlyDictionary<string, IReadOnlyList<RadioScheduledSlot>> StationSlots { get; init; }
    }
}
