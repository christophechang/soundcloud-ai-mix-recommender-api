using System;
using System.Collections.Generic;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class NowSpinningProgramLaneDto
    {
        public Mix? Mix { get; init; }

        public IReadOnlyList<NowSpinningScheduleEntryDto> Schedule { get; init; } = Array.Empty<NowSpinningScheduleEntryDto>();

        public bool LeanIgnored { get; init; }

        public bool PoolFallback { get; init; }

        public bool NoMixAvailable { get; init; }
    }
}
