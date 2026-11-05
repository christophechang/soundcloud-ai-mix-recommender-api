using System.Text.Json.Serialization;

namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Engagement.Dtos
{
    internal sealed class TokenResponseDto
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public long? ExpiresIn { get; set; }
    }
}
