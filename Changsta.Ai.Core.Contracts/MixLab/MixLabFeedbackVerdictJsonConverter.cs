using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// <see cref="MixLabFeedbackVerdict"/> serialises to the snake_case strings the architecture doc
    /// contracts (docs/architecture/mixlab-anywhere.md §5.3): <c>played</c>, <c>played_modified</c>,
    /// <c>rejected</c>, <c>unused</c>. A generic <see cref="JsonStringEnumConverter"/> with a
    /// camelCase naming policy renders <see cref="MixLabFeedbackVerdict.PlayedModified"/> as
    /// <c>playedModified</c> instead, so this converter must be registered ahead of the generic one
    /// in every options bag that touches a manifest — the blob layer's <c>MixLabJsonOptions</c> and
    /// the API's <c>MixLabRunsController.ManifestJsonOptions</c>. It lives beside
    /// <see cref="MixLabFeedbackVerdictWireValues"/>, and delegates to it, so the wire spelling has
    /// exactly one definition.
    /// </summary>
    public sealed class MixLabFeedbackVerdictJsonConverter : JsonConverter<MixLabFeedbackVerdict>
    {
        public override MixLabFeedbackVerdict Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            string? value = reader.GetString();

            if (value is not null && MixLabFeedbackVerdictWireValues.TryParse(value, out MixLabFeedbackVerdict verdict))
            {
                return verdict;
            }

            throw new JsonException($"Unknown MixLab feedback verdict '{value}'.");
        }

        public override void Write(
            Utf8JsonWriter writer,
            MixLabFeedbackVerdict value,
            JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(MixLabFeedbackVerdictWireValues.ToWireValue(value));
        }
    }
}
