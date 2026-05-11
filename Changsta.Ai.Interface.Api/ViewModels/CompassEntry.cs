using System;
using System.Collections.Generic;

namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class CompassEntry
    {
        required public string Slug { get; init; }

        required public string Title { get; init; }

        required public string Url { get; init; }

        public string? ImageUrl { get; init; }

        required public string Genre { get; init; }

        required public string Energy { get; init; }

        public int? Bpm { get; init; }

        public int? BpmMin { get; init; }

        public int? BpmMax { get; init; }

        required public double Warmth { get; init; }

        public IReadOnlyList<string> Moods { get; init; } = Array.Empty<string>();

        public DateTimeOffset? PublishedAt { get; init; }
    }
}
