using System.Text.Json.Serialization;

namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Engagement.Dtos
{
    /// <summary>
    /// The engagement subset of a track in a user-tracks page. <c>favoritings_count</c> is the like
    /// count; <c>likes_count</c> comes back empty and is deliberately not read.
    /// </summary>
    internal sealed class TrackEngagementDto
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; }

        [JsonPropertyName("playback_count")]
        public long? PlaybackCount { get; set; }

        [JsonPropertyName("favoritings_count")]
        public long? FavoritingsCount { get; set; }
    }
}
