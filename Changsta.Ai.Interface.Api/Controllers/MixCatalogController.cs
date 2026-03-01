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

            int total = allEntries.Length;
            int totalPages = (int)Math.Ceiling(total / (double)pageSize);

            GenreEntry[] items = allEntries
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToArray();

            return Ok(new CatalogPage
            {
                Items = items,
                Total = total,
                Page = page,
                PageSize = pageSize,
                TotalPages = totalPages,
            });
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

        public sealed class CatalogPage
        {
            required public GenreEntry[] Items { get; init; }

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
    }
}
