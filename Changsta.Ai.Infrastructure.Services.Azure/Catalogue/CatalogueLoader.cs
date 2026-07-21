using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Normalization;
using Changsta.Ai.Infrastructure.Services.Azure.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    /// <summary>
    /// Reads the durable blob catalogue and the live RSS feed. Both reads are individually
    /// recoverable: a blob failure degrades to RSS-only, an RSS failure degrades to blob-only.
    /// </summary>
    internal sealed class CatalogueLoader : ICatalogueLoader
    {
        private readonly IMixCatalogueProvider _rssProvider;
        private readonly IBlobMixCatalogueRepository _repository;
        private readonly ILogger _logger;

        public CatalogueLoader(
            IMixCatalogueProvider rssProvider,
            IBlobMixCatalogueRepository repository,
            ILogger logger)
        {
            _rssProvider = rssProvider ?? throw new ArgumentNullException(nameof(rssProvider));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<CatalogueLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            bool blobReadSucceeded = true;
            IReadOnlyList<Mix> blobMixes;
            string? blobETag = null;

            try
            {
                CatalogReadResult blobRead = await _repository.ReadAsync(cancellationToken).ConfigureAwait(false);
                blobMixes = blobRead.Mixes;
                blobETag = blobRead.ETag;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CatalogueMetrics.BlobReadFailures.Add(1);
                _logger.LogError(ex, "Blob catalog read failed — serving RSS-only catalog and skipping write-back to avoid overwriting intact data.");
                blobMixes = Array.Empty<Mix>();
                blobReadSucceeded = false;
            }

            blobMixes = NormalizeGenres(blobMixes, out bool blobGenresChanged);

            IReadOnlyList<Mix> rssMixes = await FetchRssSafeAsync(cancellationToken).ConfigureAwait(false);
            rssMixes = NormalizeGenres(rssMixes, out _);

            LogUnknownGenres(blobMixes, rssMixes);

            return new CatalogueLoadResult
            {
                BlobMixes = blobMixes,
                RssMixes = rssMixes,
                BlobETag = blobETag,
                BlobReadSucceeded = blobReadSucceeded,
                BlobGenresChanged = blobGenresChanged,
            };
        }

        internal static IReadOnlyList<Mix> NormalizeGenres(IReadOnlyList<Mix> mixes, out bool changed)
        {
            changed = false;
            var normalized = new Mix[mixes.Count];

            for (int i = 0; i < mixes.Count; i++)
            {
                Mix mix = mixes[i];
                string genre = GenreNormalizer.Normalize(mix.Genre);

                if (!string.Equals(mix.Genre, genre, StringComparison.Ordinal))
                {
                    changed = true;
                    normalized[i] = mix with { Genre = genre };
                }
                else
                {
                    normalized[i] = mix;
                }
            }

            return normalized;
        }

        private void LogUnknownGenres(params IReadOnlyList<Mix>[] catalogues)
        {
            var unknownGenres = catalogues
                .SelectMany(c => c)
                .Select(m => m.Genre)
                .Where(g => !string.IsNullOrWhiteSpace(g) && !GenreNormalizer.IsKnownGenre(g))
                .Select(g => GenreNormalizer.Normalize(g))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (string genre in unknownGenres)
            {
                _logger.LogWarning("Unknown genre value found during catalog sync: {Genre}", genre);
            }
        }

        private async Task<IReadOnlyList<Mix>> FetchRssSafeAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _rssProvider
                    .GetLatestAsync(int.MaxValue, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CatalogueMetrics.RssFetchFailures.Add(1);
                _logger.LogWarning(ex, "RSS feed fetch failed — proceeding with blob catalog only.");
                return Array.Empty<Mix>();
            }
        }
    }
}
