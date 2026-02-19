using System;
using System.Collections.Generic;
using System.Text;

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

        public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

        public DateTimeOffset? PublishedAt { get; set; }
    }
}
