using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    /// <summary>Per-slot tuning targets. <see cref="Key"/> must match a <c>SlotKey</c> name.</summary>
    public sealed class RadioSlotOptions
    {
        public string Key { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public int BaseBpmTarget { get; set; }

        public double WarmthTarget { get; set; }

        public IReadOnlyList<string> EnergyValues { get; set; } = Array.Empty<string>();
    }
}
