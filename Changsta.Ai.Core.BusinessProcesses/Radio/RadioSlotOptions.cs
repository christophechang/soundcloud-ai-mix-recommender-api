using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    /// <summary>Per-slot tuning targets. <see cref="Key"/> must match a <c>SlotKey</c> name.</summary>
    public sealed class RadioSlotOptions
    {
        public string Key { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// Where this slot sits in the station's own BPM distribution, 0-100. The previous
        /// BaseBpmTarget was an absolute BPM shared by every station and nudged by a per-station
        /// offset, which cannot work: the day's curve swung 62 BPM while the widest station
        /// catalogue spans 46 and the narrowest 10, so most slots targeted a tempo their station
        /// had no records at. A percentile keeps the day's arc — slow comedown, fast dead of
        /// night — and lets each station express it in whatever range it actually owns.
        /// </summary>
        public int BpmPercentile { get; set; }

        public double WarmthTarget { get; set; }

        public IReadOnlyList<string> EnergyValues { get; set; } = Array.Empty<string>();
    }
}
