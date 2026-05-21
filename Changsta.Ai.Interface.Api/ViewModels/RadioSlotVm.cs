using System.Collections.Generic;

namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class RadioSlotVm
    {
        required public int Hour { get; init; }

        required public RadioMixVm Mix { get; init; }

        public bool IsCurrent { get; init; }

        public IReadOnlyList<string> Warnings { get; init; } = System.Array.Empty<string>();
    }
}
