using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.Domain
{
    public sealed class RadioStation
    {
        required public string Id { get; init; }

        required public string Name { get; init; }

        required public string Frequency { get; init; }

        required public string Description { get; init; }

        public bool IsDefault { get; init; }

        public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();
    }
}
