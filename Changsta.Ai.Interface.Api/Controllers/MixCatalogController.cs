using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Normalization;
using Changsta.Ai.Interface.Api.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Changsta.Ai.Interface.Api.Controllers
{
    [ApiController]
    [Route("api/catalog")]
    [Produces("application/json")]
    public sealed class MixCatalogController : ControllerBase
    {
        private const int CatalogMaxItems = 200;

        private const int DefaultPageSize = 20;

        private const int MaxPageSize = 200;

        private readonly IMixCatalogueProvider _catalogueProvider;
        private readonly ICatalogFlushUseCase _flushUseCase;
        private readonly IDeleteMixUseCase _deleteMixUseCase;
        private readonly IConfiguration _configuration;

        public MixCatalogController(
            IMixCatalogueProvider catalogueProvider,
            ICatalogFlushUseCase flushUseCase,
            IDeleteMixUseCase deleteMixUseCase,
            IConfiguration configuration)
        {
            _catalogueProvider = catalogueProvider;
            _flushUseCase = flushUseCase;
            _deleteMixUseCase = deleteMixUseCase;
            _configuration = configuration;
        }

        [HttpPost("flush")]
        public async Task<IActionResult> FlushCatalogAsync(CancellationToken cancellationToken)
        {
            string? expectedSecret = _configuration["Catalog:FlushSecret"];
            if (!string.IsNullOrEmpty(expectedSecret))
            {
                if (!Request.Headers.TryGetValue("Authorization", out var authHeader)
                    || !string.Equals(authHeader.ToString(), $"Bearer {expectedSecret}", StringComparison.Ordinal))
                {
                    return Unauthorized(new { error = "Invalid or missing authorization." });
                }
            }

            await _flushUseCase.FlushAsync(cancellationToken).ConfigureAwait(false);
            return Ok(new { flushed = true });
        }

        [HttpDelete("mixes/{id}")]
        public async Task<IActionResult> DeleteMixAsync(
            [FromRoute] string id,
            CancellationToken cancellationToken)
        {
            string? expectedSecret = _configuration["Catalog:FlushSecret"];
            if (!string.IsNullOrEmpty(expectedSecret))
            {
                if (!Request.Headers.TryGetValue("Authorization", out var authHeader)
                    || !string.Equals(authHeader.ToString(), $"Bearer {expectedSecret}", StringComparison.Ordinal))
                {
                    return Unauthorized(new { error = "Invalid or missing authorization." });
                }
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                return BadRequest(new { error = "id is required." });
            }

            bool deleted = await _deleteMixUseCase.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

            if (!deleted)
            {
                return NotFound(new { error = $"No mix found with id '{id}'." });
            }

            return NoContent();
        }

        [HttpGet]
        public async Task<IActionResult> GetCatalogAsync(
            [FromQuery] string? genre,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
            {
                return BadRequest(new { error = $"page must be >= 1 and pageSize must be between 1 and {MaxPageSize}." });
            }

            IReadOnlyList<Mix> mixes = await _catalogueProvider
                .GetLatestAsync(CatalogMaxItems, cancellationToken)
                .ConfigureAwait(false);

            IEnumerable<Mix> filtered = mixes;

            if (!string.IsNullOrWhiteSpace(genre))
            {
                string normalisedQuery = NormalizeGenre(genre);
                filtered = mixes.Where(m =>
                    string.Equals(NormalizeGenre(m.Genre), normalisedQuery, StringComparison.OrdinalIgnoreCase));
            }

            var byGenre = new Dictionary<string, Dictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);

            foreach (Mix mix in filtered)
            {
                string normalisedGenre = NormalizeGenre(mix.Genre);

                if (!byGenre.TryGetValue(normalisedGenre, out Dictionary<string, SortedSet<string>>? byArtist))
                {
                    byArtist = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
                    byGenre[normalisedGenre] = byArtist;
                }

                foreach (Track track in mix.Tracklist)
                {
                    if (!byArtist.TryGetValue(track.Artist, out SortedSet<string>? titles))
                    {
                        titles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                        byArtist[track.Artist] = titles;
                    }

                    titles.Add(track.Title);
                }
            }

            GenreEntry[] allEntries = byGenre
                .Where(g => g.Value.Count > 0)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new GenreEntry
                {
                    Genre = g.Key,
                    Artists = g.Value
                        .OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(a => new ArtistEntry
                        {
                            Name = a.Key,
                            Tracks = a.Value.ToArray(),
                        })
                        .ToArray(),
                })
                .ToArray();

            return Ok(BuildPage(allEntries, page, pageSize));
        }

        [HttpGet("genres")]
        public async Task<IActionResult> GetGenresAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Mix> mixes = await _catalogueProvider
                .GetLatestAsync(CatalogMaxItems, cancellationToken)
                .ConfigureAwait(false);

            string[] genres = mixes
                .Where(m => !string.IsNullOrWhiteSpace(m.Genre))
                .Select(m => NormalizeGenre(m.Genre))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Ok(new GenresResponse { Genres = genres });
        }

        [HttpGet("mixes")]
        public async Task<IActionResult> GetMixesAsync(
            [FromQuery] string? genre,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
            {
                return BadRequest(new { error = $"page must be >= 1 and pageSize must be between 1 and {MaxPageSize}." });
            }

            IReadOnlyList<Mix> mixes = await _catalogueProvider
                .GetLatestAsync(CatalogMaxItems, cancellationToken)
                .ConfigureAwait(false);

            IEnumerable<Mix> filtered = mixes;

            if (!string.IsNullOrWhiteSpace(genre))
            {
                string normalisedQuery = NormalizeGenre(genre);
                filtered = mixes.Where(m =>
                    string.Equals(NormalizeGenre(m.Genre), normalisedQuery, StringComparison.OrdinalIgnoreCase));
            }

            Mix[] ordered = filtered
                .OrderByDescending(m => m.PublishedAt ?? DateTimeOffset.MinValue)
                .ToArray();

            return Ok(BuildPage(ordered, page, pageSize));
        }

        [HttpGet("artists")]
        public async Task<IActionResult> GetArtistsAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Mix> mixes = await _catalogueProvider
                .GetLatestAsync(CatalogMaxItems, cancellationToken)
                .ConfigureAwait(false);

            string[] artists = mixes
                .SelectMany(m => m.Tracklist)
                .Select(t => t.Artist)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Ok(new ArtistNamesResponse { Artists = artists });
        }

        [HttpGet("tracks")]
        public async Task<IActionResult> GetTracksAsync(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
            {
                return BadRequest(new { error = $"page must be >= 1 and pageSize must be between 1 and {MaxPageSize}." });
            }

            IReadOnlyList<Mix> mixes = await _catalogueProvider
                .GetLatestAsync(CatalogMaxItems, cancellationToken)
                .ConfigureAwait(false);

            TrackSummary[] allEntries = mixes
                .SelectMany(m => m.Tracklist.Select(t => (Mix: m, Track: t)))
                .GroupBy(a => (
                    Artist: a.Track.Artist.Trim().ToLowerInvariant(),
                    Title: a.Track.Title.Trim().ToLowerInvariant()))
                .Select(g =>
                {
                    Track first = g.First().Track;
                    return new TrackSummary
                    {
                        Artist = first.Artist.Trim(),
                        Title = first.Title.Trim(),
                        RecurrenceCount = g.Count(),
                        GenresSeen = g
                            .Select(a => NormalizeGenre(a.Mix.Genre))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                            .ToArray(),
                    };
                })
                .OrderByDescending(t => t.RecurrenceCount)
                .ThenBy(t => t.Artist, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Ok(BuildPage(allEntries, page, pageSize));
        }

        [HttpGet("mixes/{slug}")]
        public async Task<IActionResult> GetMixBySlugAsync(
            [FromRoute] string slug,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Mix> mixes = await _catalogueProvider
                .GetLatestAsync(CatalogMaxItems, cancellationToken)
                .ConfigureAwait(false);

            Mix? match = mixes.FirstOrDefault(m =>
                string.Equals(MixSlugHelper.ExtractSlug(m.Url), slug, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                return NotFound(new { error = $"No mix found with slug '{slug}'." });
            }

            return Ok(match);
        }

        [HttpGet("artists/{name}/mixes")]
        public async Task<IActionResult> GetMixesByArtistAsync(
            [FromRoute] string name,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
            {
                return BadRequest(new { error = $"page must be >= 1 and pageSize must be between 1 and {MaxPageSize}." });
            }

            IReadOnlyList<Mix> mixes = await _catalogueProvider
                .GetLatestAsync(CatalogMaxItems, cancellationToken)
                .ConfigureAwait(false);

            Mix[] results = mixes
                .Where(m => m.Tracklist.Any(t =>
                    string.Equals(t.Artist, name, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            if (results.Length == 0)
            {
                return NotFound(new { error = $"No mixes found for artist '{name}'." });
            }

            return Ok(BuildPage(results, page, pageSize));
        }

        private static CatalogPage<T> BuildPage<T>(T[] allEntries, int page, int pageSize)
        {
            int total = allEntries.Length;
            int totalPages = (int)Math.Ceiling(total / (double)pageSize);

            T[] items = allEntries
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToArray();

            return new CatalogPage<T>
            {
                Items = items,
                Total = total,
                Page = page,
                PageSize = pageSize,
                TotalPages = totalPages,
            };
        }

        private static string NormalizeGenre(string? genre) => GenreNormalizer.Normalize(genre);
    }
}
