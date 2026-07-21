using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    /// <summary>A single station as configured. <see cref="Genres"/> are canonical, post-normalisation values.</summary>
    public sealed class RadioStationOptions
    {
        public string Id { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;

        public string Strapline { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Frequency { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public bool IsDefault { get; set; }

        /// <summary>
        /// Applied on top of the slot's base BPM target so scoring stays meaningful relative to
        /// this station's actual BPM range.
        /// </summary>
        public int BpmOffset { get; set; }

        public IReadOnlyList<string> Genres { get; set; } = Array.Empty<string>();
    }
}
