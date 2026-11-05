namespace Changsta.Ai.Core.Domain
{
    /// <summary>
    /// Live engagement counts for one SoundCloud track. Read on demand and held in memory only;
    /// never persisted. A null count means SoundCloud omitted the field.
    /// </summary>
    public sealed class MixEngagement
    {
        required public long TrackId { get; init; }

        public long? Plays { get; init; }

        public long? Likes { get; init; }
    }
}
