using System;
using System.Collections.Generic;

namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class RadioResponse
    {
        required public DateTimeOffset GeneratedAtUtc { get; init; }

        required public string ScheduleDate { get; init; }

        required public string Timezone { get; init; }

        required public int CurrentHour { get; init; }

        required public string DefaultStationId { get; init; }

        public IReadOnlyList<RadioStationVm> Stations { get; init; } = System.Array.Empty<RadioStationVm>();
    }
}
