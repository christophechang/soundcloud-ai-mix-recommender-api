using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class RadioScheduleResultDto
    {
        required public DateTimeOffset GeneratedAtUtc { get; init; }

        required public string ScheduleDate { get; init; }

        required public string Timezone { get; init; }

        required public int CurrentHour { get; init; }

        required public string DefaultStationId { get; init; }

        public IReadOnlyList<RadioStationScheduleDto> Stations { get; init; } = Array.Empty<RadioStationScheduleDto>();

        public IReadOnlyList<string> ValidationWarnings { get; init; } = Array.Empty<string>();
    }
}
