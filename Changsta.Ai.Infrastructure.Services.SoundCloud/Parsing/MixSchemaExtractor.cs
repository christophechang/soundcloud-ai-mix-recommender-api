using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Changsta.Ai.Infrastructure.Services.SoundCloud.Models;

namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Parsing
{
    public static class MixSchemaExtractor
    {
        private const string Marker = "[changsta:mix:v1";

        public static MixSchema ExtractOrThrow(string? description, string mixIdForError)
        {
            if (!TryExtract(description, out var schema))
            {
                throw new InvalidOperationException($"Mix schema not found or invalid. mixId='{mixIdForError}'.");
            }

            if (string.IsNullOrWhiteSpace(schema.Genre))
            {
                throw new InvalidOperationException($"Mix schema missing genre. mixId='{mixIdForError}'.");
            }

            if (string.IsNullOrWhiteSpace(schema.Energy))
            {
                throw new InvalidOperationException($"Mix schema missing energy. mixId='{mixIdForError}'.");
            }

            return schema;
        }

        public static bool TryExtract(string? description, out MixSchema schema)
        {
            schema = new MixSchema();

            if (string.IsNullOrWhiteSpace(description))
            {
                return false;
            }

            int markerIndex = description.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return false;
            }

            int startJson = description.IndexOf('{', markerIndex);
            if (startJson < 0)
            {
                return false;
            }

            int endJson = FindMatchingBrace(description, startJson);
            if (endJson < 0)
            {
                return false;
            }

            string json = description.Substring(startJson, endJson - startJson + 1);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                string genre = ReadString(root, "genre");
                string energy = ReadString(root, "energy");

                (int? bpmMin, int? bpmMax) = ReadBpm(root);
                var moods = ReadStringArray(root, "moods");

                // Fix: set required members via object initializer
                schema = new MixSchema
                {
                    Genre = genre,
                    Energy = energy,
                    BpmMin = bpmMin,
                    BpmMax = bpmMax,
                    Moods = moods,
                };

                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static int FindMatchingBrace(string s, int openBraceIndex)
        {
            int depth = 0;

            for (int i = openBraceIndex; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static string ReadString(JsonElement root, string prop)
        {
            if (!root.TryGetProperty(prop, out var el))
            {
                return string.Empty;
            }

            if (el.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }

            return el.GetString() ?? string.Empty;
        }

        private static (int? min, int? max) ReadBpm(JsonElement root)
        {
            if (!root.TryGetProperty("bpm", out var el))
            {
                return (null, null);
            }

            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var single))
            {
                return (single, single);
            }

            if (el.ValueKind == JsonValueKind.Array)
            {
                var nums = el.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _))
                    .Select(x => x.GetInt32())
                    .ToArray();

                if (nums.Length == 1)
                {
                    return (nums[0], nums[0]);
                }

                if (nums.Length >= 2)
                {
                    return (nums[0], nums[1]);
                }
            }

            return (null, null);
        }

        private static IReadOnlyList<string> ReadStringArray(JsonElement root, string prop)
        {
            if (!root.TryGetProperty(prop, out var el))
            {
                return Array.Empty<string>();
            }

            if (el.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            return el.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => (x.GetString() ?? string.Empty).Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}