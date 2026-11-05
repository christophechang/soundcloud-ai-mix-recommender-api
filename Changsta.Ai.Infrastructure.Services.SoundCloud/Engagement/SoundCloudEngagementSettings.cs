namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Engagement
{
    /// <summary>
    /// Plain settings for the live play/like counts, bound from the <c>SoundCloud</c> configuration
    /// section by the API project. Missing credentials are a supported state: the stats endpoint
    /// answers 204 until both are set.
    /// </summary>
    public sealed class SoundCloudEngagementSettings
    {
        public string? ClientId { get; set; }

        public string? ClientSecret { get; set; }

        public bool StatsEnabled { get; set; } = true;

        public int StatsCacheSeconds { get; set; } = 600;

        public string? RssUrl { get; set; }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
    }
}
