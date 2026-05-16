using System;
using System.Collections.Generic;

namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class NowSpinningMixVm
    {
        required public string Id { get; init; }

        required public string Title { get; init; }

        required public string Url { get; init; }

        required public string Genre { get; init; }

        required public string Energy { get; init; }

        public int? Bpm { get; init; }

        public IReadOnlyList<string> Moods { get; init; } = Array.Empty<string>();

        public DateTimeOffset? PublishedAt { get; init; }

        public int? Duration { get; init; }
    }
}
