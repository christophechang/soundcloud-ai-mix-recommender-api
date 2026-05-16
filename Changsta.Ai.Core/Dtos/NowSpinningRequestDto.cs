using System;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class NowSpinningRequestDto
    {
        required public DateTimeOffset UtcNow { get; init; }

        public int UtcOffsetMinutes { get; init; } = 0;

        public MoodLean? MoodLean { get; init; }

        public int ScheduleCount { get; init; } = 4;
    }
}
