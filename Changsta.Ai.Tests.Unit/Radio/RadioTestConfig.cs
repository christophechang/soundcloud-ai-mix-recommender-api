using System;
using System.IO;
using System.Text.Json;
using Changsta.Ai.Core.BusinessProcesses.Radio;

namespace Changsta.Ai.Tests.Unit.Radio
{
    /// <summary>
    /// Loads the radio configuration the application actually ships
    /// (<c>Changsta.Ai.Interface.Api/config/radio.json</c>) so the scheduling tests run against
    /// real station and slot data. A bad edit to that file fails these tests rather than only
    /// surfacing at startup.
    /// </summary>
    internal static class RadioTestConfig
    {
        private static readonly Lazy<RadioOptions> LazyOptions = new(Load);

        internal static RadioOptions Options => LazyOptions.Value;

        internal static RadioDefinitions Definitions { get; } = new RadioDefinitions(Options);

        internal static string ConfigPath => FindConfigPath();

        private static JsonSerializerOptions SerializerOptions => new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private static RadioOptions Load()
        {
            using FileStream stream = File.OpenRead(FindConfigPath());
            using JsonDocument document = JsonDocument.Parse(stream);

            JsonElement radio = document.RootElement.GetProperty("Radio");

            var options = new RadioOptions
            {
                Stations = JsonSerializer.Deserialize<RadioStationOptions[]>(
                    radio.GetProperty("Stations").GetRawText(),
                    SerializerOptions) ?? Array.Empty<RadioStationOptions>(),
                Slots = JsonSerializer.Deserialize<RadioSlotOptions[]>(
                    radio.GetProperty("Slots").GetRawText(),
                    SerializerOptions) ?? Array.Empty<RadioSlotOptions>(),
            };

            return options;
        }

        private static string FindConfigPath()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                string candidate = Path.Combine(
                    directory.FullName,
                    "Changsta.Ai.Interface.Api",
                    "config",
                    "radio.json");

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException(
                "Could not locate Changsta.Ai.Interface.Api/config/radio.json from " + AppContext.BaseDirectory);
        }
    }
}
