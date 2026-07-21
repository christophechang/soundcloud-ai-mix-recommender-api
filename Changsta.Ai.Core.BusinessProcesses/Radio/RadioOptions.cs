using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    /// <summary>
    /// Product-tuned radio configuration, bound from <c>config/radio.json</c>. Stations and their
    /// per-slot targets are product decisions, so changing one should not need a redeploy. The
    /// scoring algorithm itself — slot hour boundaries, day adjustments, energy vocabulary — stays
    /// in code, because it is coupled to <see cref="RadioSlotScorer"/> rather than tuneable.
    /// </summary>
    public sealed class RadioOptions
    {
        public const string SectionName = "Radio";

        public IReadOnlyList<RadioStationOptions> Stations { get; set; } = Array.Empty<RadioStationOptions>();

        public IReadOnlyList<RadioSlotOptions> Slots { get; set; } = Array.Empty<RadioSlotOptions>();
    }
}
