using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Infrastructure.Services.Azure.MixLab
{
    /// <summary>
    /// <see cref="MixLabFeedbackVerdict"/> serialises to the snake_case strings the architecture
    /// doc contracts (docs/architecture/mixlab-anywhere.md §5.3): <c>played</c>,
    /// <c>played_modified</c>, <c>rejected</c>, <c>unused</c>. A generic
    /// <see cref="JsonStringEnumConverter"/> with a camelCase naming policy would render
    /// <see cref="MixLabFeedbackVerdict.PlayedModified"/> as <c>playedModified</c> instead, so this
    /// converter is registered ahead of the generic one in <see cref="MixLabJsonOptions"/>.
    /// </summary>
    internal sealed class MixLabFeedbackVerdictJsonConverter : JsonConverter<MixLabFeedbackVerdict>
    {
        public override MixLabFeedbackVerdict Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            string? value = reader.GetString();

            return value switch
            {
                "played" => MixLabFeedbackVerdict.Played,
                "played_modified" => MixLabFeedbackVerdict.PlayedModified,
                "rejected" => MixLabFeedbackVerdict.Rejected,
                "unused" => MixLabFeedbackVerdict.Unused,
                _ => throw new JsonException($"Unknown MixLab feedback verdict '{value}'."),
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            MixLabFeedbackVerdict value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value switch
            {
                MixLabFeedbackVerdict.Played => "played",
                MixLabFeedbackVerdict.PlayedModified => "played_modified",
                MixLabFeedbackVerdict.Rejected => "rejected",
                MixLabFeedbackVerdict.Unused => "unused",
                _ => throw new JsonException($"Unknown MixLab feedback verdict '{value}'."),
            });
        }
    }
}
