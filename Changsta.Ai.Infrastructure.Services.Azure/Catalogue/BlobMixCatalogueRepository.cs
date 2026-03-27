using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.Azure.Configuration;
using Changsta.Ai.Infrastructure.Services.Azure.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    internal sealed class BlobMixCatalogueRepository : IBlobMixCatalogueRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new TrackListJsonConverter() },
        };

        private readonly BlobContainerClient _containerClient;
        private readonly string _blobName;
        private readonly ILogger<BlobMixCatalogueRepository> _logger;

        public BlobMixCatalogueRepository(
            IOptions<BlobCatalogOptions> options,
            ILogger<BlobMixCatalogueRepository> logger)
        {
            var resolved = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(resolved.ContainerName))
            {
                throw new InvalidOperationException("Azure:BlobCatalog:ContainerName is not configured.");
            }

            if (string.IsNullOrWhiteSpace(resolved.BlobName))
            {
                throw new InvalidOperationException("Azure:BlobCatalog:BlobName is not configured.");
            }

            if (!string.IsNullOrWhiteSpace(resolved.ConnectionString))
            {
                _containerClient = new BlobContainerClient(resolved.ConnectionString, resolved.ContainerName);
            }
            else if (!string.IsNullOrWhiteSpace(resolved.ServiceEndpoint))
            {
                // Production path: Managed Identity via DefaultAzureCredential.
                // Requires Azure.Identity package — add Azure.Identity to this project to enable.
                throw new InvalidOperationException(
                    "Azure:BlobCatalog:ServiceEndpoint is set but Managed Identity authentication is not yet implemented. " +
                    "Add the Azure.Identity package and replace this throw with: " +
                    "new BlobContainerClient(new Uri(resolved.ServiceEndpoint + \"/\" + resolved.ContainerName), new DefaultAzureCredential())");
            }
            else
            {
                throw new InvalidOperationException(
                    "Azure:BlobCatalog: either ConnectionString (dev) or ServiceEndpoint (prod) must be configured.");
            }

            _blobName = resolved.BlobName;
        }

        public async Task<IReadOnlyList<Mix>> ReadAsync(CancellationToken cancellationToken)
        {
            try
            {
                var blobClient = _containerClient.GetBlobClient(_blobName);
                var download = await blobClient
                    .DownloadContentAsync(cancellationToken)
                    .ConfigureAwait(false);

                var document = JsonSerializer.Deserialize<MixCatalogDocument>(
                    download.Value.Content.ToStream(),
                    JsonOptions);

                return document?.Mixes ?? Array.Empty<Mix>();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogInformation("Blob catalog not found — starting with empty catalog on first run.");
                return Array.Empty<Mix>();
            }
        }

        public async Task WriteAsync(IReadOnlyList<Mix> mixes, CancellationToken cancellationToken)
        {
            try
            {
                await _containerClient
                    .CreateIfNotExistsAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var document = new MixCatalogDocument
                {
                    SchemaVersion = 1,
                    LastUpdatedAt = DateTimeOffset.UtcNow,
                    Mixes = mixes,
                };

                using var stream = new MemoryStream();
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                stream.Position = 0;

                var blobClient = _containerClient.GetBlobClient(_blobName);
                await blobClient
                    .UploadAsync(stream, overwrite: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write blob catalog — merged result was still returned to caller.");
            }
        }

        private sealed class TrackListJsonConverter : JsonConverter<IReadOnlyList<Track>>
        {
            public override IReadOnlyList<Track> Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    return Array.Empty<Track>();
                }

                var tracks = new List<Track>();

                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        // v1 format: array of "Artist - Title" strings
                        string raw = reader.GetString() ?? string.Empty;
                        int sep = raw.IndexOf(" - ", StringComparison.Ordinal);

                        if (sep >= 0)
                        {
                            tracks.Add(new Track
                            {
                                Artist = raw.Substring(0, sep).Trim(),
                                Title = raw.Substring(sep + 3).Trim(),
                            });
                        }
                    }
                    else if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        // v2 format: array of { "artist": "...", "title": "..." }
                        string artist = string.Empty;
                        string title = string.Empty;

                        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                        {
                            if (reader.TokenType != JsonTokenType.PropertyName)
                            {
                                continue;
                            }

                            string propName = reader.GetString() ?? string.Empty;
                            reader.Read();

                            if (string.Equals(propName, "artist", StringComparison.OrdinalIgnoreCase))
                            {
                                artist = reader.GetString() ?? string.Empty;
                            }
                            else if (string.Equals(propName, "title", StringComparison.OrdinalIgnoreCase))
                            {
                                title = reader.GetString() ?? string.Empty;
                            }
                        }

                        tracks.Add(new Track { Artist = artist, Title = title });
                    }
                }

                return tracks;
            }

            public override void Write(
                Utf8JsonWriter writer,
                IReadOnlyList<Track> value,
                JsonSerializerOptions options)
            {
                writer.WriteStartArray();

                for (int i = 0; i < value.Count; i++)
                {
                    writer.WriteStartObject();
                    writer.WriteString("artist", value[i].Artist);
                    writer.WriteString("title", value[i].Title);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }
        }
    }
}
