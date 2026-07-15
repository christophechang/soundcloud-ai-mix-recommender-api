using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Validates run flags against a strict allow-list, freezes the target upload (resolving
    /// <c>latest</c> to a concrete id at enqueue time), and writes a queued run manifest. See
    /// issue #130.
    /// </summary>
    public sealed class EnqueueMixLabRunUseCase : IEnqueueMixLabRunUseCase
    {
        private const string LatestUpload = "latest";

        private const int MinMixLength = 20;

        private const int MaxMixLength = 240;

        private const int MaxIntentLength = 500;

        private const double MinBpmValue = 40;

        private const double MaxBpmValue = 260;

        private const int MinYearValue = 1900;

        private const int MaxYearValue = 2100;

        private static readonly HashSet<string> AllowedKeys = new(StringComparer.Ordinal)
        {
            "genre",
            "mode",
            "risk",
            "directions",
            "intent",
            "mixLength",
            "resequence",
            "deep",
            "stage1Seed",
            "playlist",
            "minBpm",
            "maxBpm",
            "minYear",
            "maxYear",
        };

        private static readonly HashSet<string> Modes = new(StringComparer.Ordinal) { "unplayed", "all", "played" };

        private static readonly HashSet<string> Risks = new(StringComparer.Ordinal) { "low", "medium", "high" };

        private static readonly HashSet<string> DirectionValues = new(StringComparer.Ordinal) { "mixed", "off", "only" };

        private readonly IMixLabRunRepository _runs;
        private readonly IMixLabUploadRepository _uploads;
        private readonly ILogger<EnqueueMixLabRunUseCase> _logger;

        public EnqueueMixLabRunUseCase(
            IMixLabRunRepository runs,
            IMixLabUploadRepository uploads,
            ILogger<EnqueueMixLabRunUseCase> logger)
        {
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
            _uploads = uploads ?? throw new ArgumentNullException(nameof(uploads));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<EnqueueMixLabRunResult> EnqueueAsync(JsonElement flags, string? uploadId, CancellationToken cancellationToken)
        {
            if (!TryBuildFlags(flags, out MixLabRunFlags? runFlags, out string? flagsError))
            {
                return Invalid(flagsError!);
            }

            if (string.IsNullOrWhiteSpace(uploadId))
            {
                return Invalid("uploadId is required.");
            }

            string resolvedUploadId;
            if (string.Equals(uploadId, LatestUpload, StringComparison.Ordinal))
            {
                string? latest = await _uploads.GetLatestIdAsync(cancellationToken).ConfigureAwait(false);
                if (latest is null)
                {
                    return new EnqueueMixLabRunResult
                    {
                        Outcome = EnqueueMixLabRunResult.EnqueueOutcome.NoUploadsAvailable,
                        ErrorMessage = "No uploads exist yet; upload a collection before enqueuing a run.",
                    };
                }

                resolvedUploadId = latest;
            }
            else
            {
                if (!await UploadExistsAsync(uploadId, cancellationToken).ConfigureAwait(false))
                {
                    return new EnqueueMixLabRunResult
                    {
                        Outcome = EnqueueMixLabRunResult.EnqueueOutcome.UnknownUpload,
                        ErrorMessage = $"Upload '{uploadId}' does not exist.",
                    };
                }

                resolvedUploadId = uploadId;
            }

            MixLabRun run = await _runs
                .CreateQueuedAsync(runFlags!, resolvedUploadId, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Enqueued MixLab run {RunId} for upload {UploadId} (genre {Genre}).",
                run.RunId,
                resolvedUploadId,
                runFlags!.Genre);

            return new EnqueueMixLabRunResult
            {
                Outcome = EnqueueMixLabRunResult.EnqueueOutcome.Created,
                RunId = run.RunId,
            };
        }

        private static EnqueueMixLabRunResult Invalid(string message)
        {
            return new EnqueueMixLabRunResult
            {
                Outcome = EnqueueMixLabRunResult.EnqueueOutcome.InvalidRequest,
                ErrorMessage = message,
            };
        }

        private static bool TryBuildFlags(JsonElement flags, out MixLabRunFlags? built, out string? error)
        {
            built = null;
            error = null;

            if (flags.ValueKind != JsonValueKind.Object)
            {
                error = "flags must be a JSON object.";
                return false;
            }

            foreach (JsonProperty property in flags.EnumerateObject())
            {
                if (!AllowedKeys.Contains(property.Name))
                {
                    error = $"Unknown flag '{property.Name}'.";
                    return false;
                }
            }

            if (!TryReadRequiredString(flags, "genre", out string? genre, out error)
                || !TryReadAllowedString(flags, "mode", Modes, out string? mode, out error)
                || !TryReadAllowedString(flags, "risk", Risks, out string? risk, out error)
                || !TryReadAllowedString(flags, "directions", DirectionValues, out string? directions, out error))
            {
                return false;
            }

            if (!TryReadOptionalIntent(flags, out string? intent, out error)
                || !TryReadOptionalBoundedInt(flags, "mixLength", MinMixLength, MaxMixLength, out int? mixLength, out error)
                || !TryReadOptionalBool(flags, "resequence", out bool resequence, out error)
                || !TryReadOptionalBool(flags, "deep", out bool deep, out error)
                || !TryReadOptionalInt(flags, "stage1Seed", out int? stage1Seed, out error))
            {
                return false;
            }

            if (!TryReadOptionalNonEmptyString(flags, "playlist", out string? playlist, out error)
                || !TryReadOptionalBoundedDouble(flags, "minBpm", MinBpmValue, MaxBpmValue, out double? minBpm, out error)
                || !TryReadOptionalBoundedDouble(flags, "maxBpm", MinBpmValue, MaxBpmValue, out double? maxBpm, out error)
                || !TryReadOptionalBoundedInt(flags, "minYear", MinYearValue, MaxYearValue, out int? minYear, out error)
                || !TryReadOptionalBoundedInt(flags, "maxYear", MinYearValue, MaxYearValue, out int? maxYear, out error))
            {
                return false;
            }

            if (minBpm is double lo && maxBpm is double hi && lo > hi)
            {
                error = "'minBpm' must be less than or equal to 'maxBpm'.";
                return false;
            }

            if (minYear is int loYear && maxYear is int hiYear && loYear > hiYear)
            {
                error = "'minYear' must be less than or equal to 'maxYear'.";
                return false;
            }

            built = new MixLabRunFlags
            {
                Genre = genre!,
                Mode = mode!,
                Risk = risk!,
                Directions = directions!,
                Intent = intent,
                MixLength = mixLength,
                Resequence = resequence,
                Deep = deep,
                Stage1Seed = stage1Seed,
                Playlist = playlist,
                MinBpm = minBpm,
                MaxBpm = maxBpm,
                MinYear = minYear,
                MaxYear = maxYear,
            };
            return true;
        }

        private static bool TryReadRequiredString(JsonElement flags, string key, out string? value, out string? error)
        {
            value = null;
            error = null;

            if (!flags.TryGetProperty(key, out JsonElement element))
            {
                error = $"'{key}' is required.";
                return false;
            }

            if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
            {
                error = $"'{key}' must be a non-empty string.";
                return false;
            }

            value = element.GetString();
            return true;
        }

        private static bool TryReadAllowedString(
            JsonElement flags,
            string key,
            HashSet<string> allowed,
            out string? value,
            out string? error)
        {
            if (!TryReadRequiredString(flags, key, out value, out error))
            {
                return false;
            }

            if (!allowed.Contains(value!))
            {
                error = $"'{key}' must be one of: {string.Join(", ", allowed)}.";
                value = null;
                return false;
            }

            return true;
        }

        private static bool TryReadOptionalIntent(JsonElement flags, out string? value, out string? error)
        {
            value = null;
            error = null;

            if (!flags.TryGetProperty("intent", out JsonElement element) || element.ValueKind == JsonValueKind.Null)
            {
                return true;
            }

            if (element.ValueKind != JsonValueKind.String)
            {
                error = "'intent' must be a string.";
                return false;
            }

            string intent = element.GetString() ?? string.Empty;
            if (intent.Length > MaxIntentLength)
            {
                error = $"'intent' must be at most {MaxIntentLength} characters.";
                return false;
            }

            value = intent;
            return true;
        }

        private static bool TryReadOptionalInt(JsonElement flags, string key, out int? value, out string? error)
        {
            value = null;
            error = null;

            if (!flags.TryGetProperty(key, out JsonElement element) || element.ValueKind == JsonValueKind.Null)
            {
                return true;
            }

            if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int parsed))
            {
                error = $"'{key}' must be an integer.";
                return false;
            }

            value = parsed;
            return true;
        }

        private static bool TryReadOptionalBoundedInt(
            JsonElement flags,
            string key,
            int min,
            int max,
            out int? value,
            out string? error)
        {
            if (!TryReadOptionalInt(flags, key, out value, out error))
            {
                return false;
            }

            if (value is int parsed && (parsed < min || parsed > max))
            {
                error = $"'{key}' must be between {min} and {max}.";
                value = null;
                return false;
            }

            return true;
        }

        private static bool TryReadOptionalBool(JsonElement flags, string key, out bool value, out string? error)
        {
            value = false;
            error = null;

            if (!flags.TryGetProperty(key, out JsonElement element) || element.ValueKind == JsonValueKind.Null)
            {
                return true;
            }

            if (element.ValueKind != JsonValueKind.True && element.ValueKind != JsonValueKind.False)
            {
                error = $"'{key}' must be a boolean.";
                return false;
            }

            value = element.GetBoolean();
            return true;
        }

        private static bool TryReadOptionalNonEmptyString(JsonElement flags, string key, out string? value, out string? error)
        {
            value = null;
            error = null;

            if (!flags.TryGetProperty(key, out JsonElement element) || element.ValueKind == JsonValueKind.Null)
            {
                return true;
            }

            if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
            {
                error = $"'{key}' must be a non-empty string.";
                return false;
            }

            value = element.GetString();
            return true;
        }

        private static bool TryReadOptionalBoundedDouble(
            JsonElement flags,
            string key,
            double min,
            double max,
            out double? value,
            out string? error)
        {
            value = null;
            error = null;

            if (!flags.TryGetProperty(key, out JsonElement element) || element.ValueKind == JsonValueKind.Null)
            {
                return true;
            }

            if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out double parsed))
            {
                error = $"'{key}' must be a number.";
                return false;
            }

            if (parsed < min || parsed > max)
            {
                error = $"'{key}' must be between {min} and {max}.";
                return false;
            }

            value = parsed;
            return true;
        }

        private async Task<bool> UploadExistsAsync(string uploadId, CancellationToken cancellationToken)
        {
            IReadOnlyList<MixLabUpload> uploads = await _uploads.GetIndexAsync(cancellationToken).ConfigureAwait(false);
            foreach (MixLabUpload upload in uploads)
            {
                if (string.Equals(upload.UploadId, uploadId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
