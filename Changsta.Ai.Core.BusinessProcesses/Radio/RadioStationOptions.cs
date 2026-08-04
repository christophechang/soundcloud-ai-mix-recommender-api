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

        public IReadOnlyList<string> Genres { get; set; } = Array.Empty<string>();
    }
}
