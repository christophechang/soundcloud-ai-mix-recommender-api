using System;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class NowSpinningProgramRequestDto
    {
        required public DateTimeOffset UtcNow { get; init; }

        public int UtcOffsetMinutes { get; init; } = 0;

        public int ScheduleCount { get; init; } = 4;
    }
}
