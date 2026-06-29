using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Exceptions;
using Changsta.Ai.Infrastructure.Services.Azure.Configuration;
using Changsta.Ai.Infrastructure.Services.Azure.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    internal sealed class BlobMixCatalogueRepository : IBlobMixCatalogueRepository
    {
        internal static readonly JsonSerializerOptions JsonOptions = new()
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
            TokenCredential credential,
            ILogger<BlobMixCatalogueRepository> logger)
        {
            var resolved = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            credential = credential ?? throw new ArgumentNullException(nameof(credential));

            // BlobCatalogOptions is validated at startup by BlobCatalogOptionsValidator
            // (ValidateOnStart), so no constructor-time re-validation is needed here (#45). The
            // container client is built once and held for the lifetime of this singleton (#44).
            _containerClient = BlobContainerClientFactory.Create(resolved, credential);
            _blobName = resolved.BlobName;
        }

        public async Task<CatalogReadResult> ReadAsync(CancellationToken cancellationToken)
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

                return new CatalogReadResult(
                    document?.Mixes ?? Array.Empty<Mix>(),
                    download.Value.Details.ETag.ToString());
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogInformation("Blob catalog not found — starting with empty catalog on first run.");
                return new CatalogReadResult(Array.Empty<Mix>(), eTag: null);
            }
        }

        public async Task WriteAsync(IReadOnlyList<Mix> mixes, string? expectedETag, CancellationToken cancellationToken)
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

            // Optimistic concurrency: If-Match the read ETag, or create-only (If-None-Match: *)
            // when there was no prior blob. A 412 (precondition failed) or 409 (blob already
            // exists on a create-only write) means another writer changed the blob concurrently.
            var conditions = new BlobRequestConditions();
            if (expectedETag is null)
            {
                conditions.IfNoneMatch = ETag.All;
            }
            else
            {
                conditions.IfMatch = new ETag(expectedETag);
            }

            var blobClient = _containerClient.GetBlobClient(_blobName);

            try
            {
                await blobClient
                    .UploadAsync(stream, new BlobUploadOptions { Conditions = conditions }, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (RequestFailedException ex) when (ex.Status == 412 || ex.Status == 409)
            {
                _logger.LogWarning(
                    ex,
                    "Blob catalog write conflict (status {Status}) — concurrent modification detected. expectedETag={ExpectedETag}",
                    ex.Status,
                    expectedETag ?? "(create-only)");
                throw new CatalogConcurrencyException(
                    "Blob catalog write was rejected because the blob changed concurrently.", ex);
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
                        // v2 format: array of { "artist": "...", "title": "...", "cuePointSeconds": 248? }
                        string artist = string.Empty;
                        string title = string.Empty;
                        int? cuePointSeconds = null;

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
                            else if (string.Equals(propName, "cuePointSeconds", StringComparison.OrdinalIgnoreCase))
                            {
                                if (reader.TokenType == JsonTokenType.Number)
                                {
                                    cuePointSeconds = reader.GetInt32();
                                }
                            }
                        }

                        tracks.Add(new Track { Artist = artist, Title = title, CuePointSeconds = cuePointSeconds });
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

                    int? cuePointSeconds = value[i].CuePointSeconds;
                    if (cuePointSeconds.HasValue)
                    {
                        writer.WriteNumber("cuePointSeconds", cuePointSeconds.Value);
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }
        }
    }
}
