using System.Text.Json;
using System.Text.Json.Serialization;

namespace Changsta.Ai.Infrastructure.Services.Azure.MixLab
{
    /// <summary>
    /// Shared <see cref="JsonSerializerOptions"/> for all MixLab blob documents: camelCase
    /// property names matching docs/architecture/mixlab-anywhere.md §5 exactly, plus the
    /// feedback-verdict converter (which must run before the generic string-enum converter — see
    /// <see cref="MixLabFeedbackVerdictJsonConverter"/>).
    /// </summary>
    internal static class MixLabJsonOptions
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new MixLabFeedbackVerdictJsonConverter(),
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            },
        };
    }
}
