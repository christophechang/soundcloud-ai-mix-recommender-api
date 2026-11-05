using System.Text.Json.Serialization;

namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Engagement.Dtos
{
    /// <summary>
    /// One <c>linked_partitioning</c> page of <c>/users/{id}/tracks</c>. <see cref="Collection"/>
    /// has no default so a page without it is detectable and treated as a failure, unlike
    /// Setfolio's page DTO, which defaults it to an empty list.
    /// </summary>
    internal sealed class TrackPageDto
    {
        [JsonPropertyName("collection")]
        public List<TrackEngagementDto?>? Collection { get; set; }

        [JsonPropertyName("next_href")]
        public string? NextHref { get; set; }
    }
}
