using System;

namespace Changsta.Ai.Core.Domain
{
    public sealed class RelatedMixRef
    {
        required public string Title { get; init; }

        required public string Url { get; init; }

        public string? ArtworkUrl { get; init; }
    }
}
