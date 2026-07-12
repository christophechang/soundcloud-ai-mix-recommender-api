namespace Changsta.Ai.Core.Domain.MixLab
{
    /// <summary>
    /// A concept's outcome once fed back from the field. See
    /// docs/architecture/mixlab-anywhere.md §5.3. Serialises to
    /// <c>played|played_modified|rejected|unused</c> via the dedicated
    /// <see cref="Changsta.Ai.Infrastructure.Services.Azure.MixLab.MixLabFeedbackVerdictJsonConverter"/>
    /// (the snake_case value for <see cref="PlayedModified"/> cannot be produced by the generic
    /// camelCase string-enum converter used elsewhere).
    /// </summary>
    public enum MixLabFeedbackVerdict
    {
        Played,
        PlayedModified,
        Rejected,
        Unused,
    }
}
