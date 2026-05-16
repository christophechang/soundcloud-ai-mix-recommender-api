using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class NowSpinningProgramRequestDto
    {
        required public DateTimeOffset UtcNow { get; init; }

        public int UtcOffsetMinutes { get; init; } = 0;

        public IReadOnlyList<string> SkipIds { get; init; } = Array.Empty<string>();

        public int ScheduleCount { get; init; } = 4;
    }
}
