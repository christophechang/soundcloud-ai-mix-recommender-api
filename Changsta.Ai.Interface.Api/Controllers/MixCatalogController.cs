using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Changsta.Ai.Interface.Api.Controllers
{
    [ApiController]
    [Route("api/catalog")]
    [Produces("application/json")]
    public sealed class MixCatalogController : ControllerBase
    {
        private const int CatalogMaxItems = 200;

        private const int DefaultPageSize = 20;

        private const int MaxPageSize = 100;

        private static readonly Dictionary<string, string> GenreNormalisations = new(StringComparer.OrdinalIgnoreCase)
        {
            { "deephouse", "deep-house" },
            { "ukbass", "uk-bass" },
        };

        private readonly IMixCatalogueProvider _catalogueProvider;

        public MixCatalogController(IMixCatalogueProvider catalogueProvider)
        {
            _catalogueProvider = catalogueProvider;
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
                return BadRequest(new { error = "page must be >= 1 and pageSize must be between 1 and 100." });
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

        [HttpGet("mixes")]
        public async Task<IActionResult> GetMixesAsync(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
            {
                return BadRequest(new { error = "page must be >= 1 and pageSize must be between 1 and 100." });
            }

            IReadOnlyList<Mix> mixes = await _catalogueProvider
                .GetLatestAsync(CatalogMaxItems, cancellationToken)
                .ConfigureAwait(false);

            Mix[] ordered = mixes
                .OrderByDescending(m => m.PublishedAt ?? DateTimeOffset.MinValue)
                .ToArray();

            return Ok(BuildPage(ordered, page, pageSize));
        }

        [HttpGet("artists")]
        public async Task<IActionResult> GetArtistsAsync(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
            {
                return BadRequest(new { error = "page must be >= 1 and pageSize must be between 1 and 100." });
            }

            IReadOnlyList<Mix> mixes = await _catalogueProvider
                .GetLatestAsync(CatalogMaxItems, cancellationToken)
                .ConfigureAwait(false);

            var byArtist = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (Mix mix in mixes)
            {
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

            ArtistSummary[] allEntries = byArtist
                .Select(a => new ArtistSummary
                {
                    Name = a.Key,
                    TrackCount = a.Value.Count,
                    Tracks = a.Value.ToArray(),
                })
                .OrderByDescending(a => a.TrackCount)
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Ok(BuildPage(allEntries, page, pageSize));
        }

        [HttpGet("tracks")]
        public async Task<IActionResult> GetTracksAsync(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
            {
                return BadRequest(new { error = "page must be >= 1 and pageSize must be between 1 and 100." });
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

        [HttpGet("artists/{name}/mixes")]
        public async Task<IActionResult> GetMixesByArtistAsync(
            [FromRoute] string name,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Mix> mixes = await _catalogueProvider
                .GetLatestAsync(CatalogMaxItems, cancellationToken)
                .ConfigureAwait(false);

            Mix[] results = mixes
                .Where(m => m.Tracklist.Any(t =>
                    string.Equals(t.Artist, name, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            return Ok(results);
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

        private static string NormalizeGenre(string genre) =>
            GenreNormalisations.TryGetValue(genre.Replace("-", string.Empty, StringComparison.Ordinal), out string? canonical)
                ? canonical
                : genre;

        public sealed class ArtistEntry
        {
            required public string Name { get; init; }

            required public string[] Tracks { get; init; }
        }

        public sealed class ArtistSummary
        {
            required public string Name { get; init; }

            required public int TrackCount { get; init; }

            required public string[] Tracks { get; init; }
        }

        public sealed class CatalogPage<T>
        {
            required public T[] Items { get; init; }

            required public int Total { get; init; }

            required public int Page { get; init; }

            required public int PageSize { get; init; }

            required public int TotalPages { get; init; }
        }

        public sealed class GenreEntry
        {
            required public string Genre { get; init; }

            required public ArtistEntry[] Artists { get; init; }
        }

        public sealed class TrackSummary
        {
            required public string Artist { get; init; }

            required public string[] GenresSeen { get; init; }

            required public int RecurrenceCount { get; init; }

            required public string Title { get; init; }
        }
    }
}
