using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class NowSpinningRequestDto
    {
        required public DateTimeOffset UtcNow { get; init; }

        public int UtcOffsetMinutes { get; init; } = 0;

        public MoodLean? MoodLean { get; init; }

        public IReadOnlyList<string> SkipIds { get; init; } = Array.Empty<string>();

        public int ScheduleCount { get; init; } = 4;
    }
}
