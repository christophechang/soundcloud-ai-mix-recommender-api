using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class NowSpinningProgramLaneVm
    {
        public NowSpinningMixVm? Mix { get; init; }

        public IReadOnlyList<NowSpinningScheduleEntryVm> Schedule { get; init; } = Array.Empty<NowSpinningScheduleEntryVm>();

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool LeanIgnored { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool PoolFallback { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool NoMixAvailable { get; init; }
    }
}
