using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.Domain
{
    public sealed class Mix
    {
        required public string Id { get; init; }

        required public string Title { get; init; }

        required public string Url { get; init; }

        public string? Description { get; init; }

        public string? IntroText { get; init; }

        public IReadOnlyList<string> Tracklist { get; init; } = Array.Empty<string>();

        public DateTimeOffset? PublishedAt { get; init; }

        required public string Genre { get; init; }

        required public string Energy { get; init; }

        public int? BpmMin { get; init; }

        public int? BpmMax { get; init; }

        public IReadOnlyList<string> Moods { get; init; } = Array.Empty<string>();
    }
}