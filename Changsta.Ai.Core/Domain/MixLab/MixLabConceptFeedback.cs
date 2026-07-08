using System;

namespace Changsta.Ai.Core.Domain.MixLab
{
    /// <summary>
    /// Feedback merged onto a concept inside its run manifest (<c>POST
    /// /api/mixlab/runs/{id}/concepts/{conceptId}/feedback</c>). See
    /// docs/architecture/mixlab-anywhere.md §5.1 and §5.3.
    /// </summary>
    public sealed record MixLabConceptFeedback
    {
        public MixLabFeedbackVerdict? Verdict { get; init; }

        public int? Rating { get; init; }

        public string? Notes { get; init; }

        public string? PublishedMixSlug { get; init; }

        required public DateTimeOffset RecordedAt { get; init; }
    }
}
