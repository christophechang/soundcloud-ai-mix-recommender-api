using System;

namespace Changsta.Ai.Core.Domain.MixLab
{
    /// <summary>
    /// A <c>feedback/pending.json</c> entry: feedback not yet synced into the engine's concept
    /// history by the worker. See docs/architecture/mixlab-anywhere.md §5.3.
    /// </summary>
    public sealed record MixLabFeedbackEvent
    {
        required public string EventId { get; init; }

        required public string RunId { get; init; }

        required public string ConceptId { get; init; }

        public MixLabFeedbackVerdict? Verdict { get; init; }

        public int? Rating { get; init; }

        public string? Notes { get; init; }

        public string? PublishedMixSlug { get; init; }

        required public DateTimeOffset RecordedAt { get; init; }
    }
}
