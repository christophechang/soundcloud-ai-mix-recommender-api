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

        /// <summary>
        /// The operator has starred this cut as one they intend to make ("in session" in
        /// mixlab-web). Set only through
        /// <c>PUT /api/mixlab/runs/{id}/concepts/{conceptId}/shortlist</c>; deliberately outside the
        /// feedback loop, so nothing about it reaches engine history. Absent on manifests written
        /// before this field existed, which reads as <see langword="false"/>.
        /// </summary>
        public bool Shortlisted { get; init; }
    }
}
