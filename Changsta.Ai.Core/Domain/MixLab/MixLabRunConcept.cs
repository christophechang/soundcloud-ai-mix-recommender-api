namespace Changsta.Ai.Core.Domain.MixLab
{
    /// <summary>
    /// A concept summary attached to a run manifest at completion (id + title from
    /// <c>summary.json</c>); feedback is filled in later. The <see cref="ConceptId"/> must be
    /// identical to the id minted for the engine's <c>ConceptRecord</c> history entry — see
    /// docs/architecture/mixlab-anywhere.md §5.2's "conceptId unification" note (M1).
    /// </summary>
    public sealed record MixLabRunConcept
    {
        required public string ConceptId { get; init; }

        required public string Title { get; init; }

        public MixLabConceptFeedback? Feedback { get; init; }
    }
}
