using System.Collections.Generic;

namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class RadioSlotVm
    {
        required public int Hour { get; init; }

        required public RadioMixVm Mix { get; init; }

        public bool IsCurrent { get; init; }

        public IReadOnlyList<string> Warnings { get; init; } = System.Array.Empty<string>();

        /// <summary>
        /// Scheduling rules the scheduler had to drop to fill this slot, in the order it dropped
        /// them. Empty means the slot was filled on a clean match. A non-empty list is the
        /// scheduler saying the placement is a compromise — clients should not present the slot
        /// as a considered choice when it is set.
        /// </summary>
        public IReadOnlyList<string> RelaxedRules { get; init; } = System.Array.Empty<string>();
    }
}
