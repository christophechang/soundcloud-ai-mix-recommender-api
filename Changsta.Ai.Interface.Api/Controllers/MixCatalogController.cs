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
        public async Task<IActionResult> GetTrackHierarchyAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<Mix> mixes = await _catalogueProvider
                .GetLatestAsync(CatalogMaxItems, cancellationToken)
                .ConfigureAwait(false);

            var byGenre = new Dictionary<string, Dictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);

            foreach (Mix mix in mixes)
            {
                string genre = NormalizeGenre(mix.Genre);

                if (!byGenre.TryGetValue(genre, out Dictionary<string, SortedSet<string>>? byArtist))
                {
                    byArtist = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
                    byGenre[genre] = byArtist;
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

            var result = byGenre
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    Genre = g.Key,
                    Artists = g.Value
                        .OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(a => new
                        {
                            Name = a.Key,
                            Titles = a.Value.ToList(),
                        })
                        .ToList(),
                })
                .ToList();

            return Ok(result);
        }

        private static string NormalizeGenre(string genre) =>
            GenreNormalisations.TryGetValue(genre.Replace("-", string.Empty, StringComparison.Ordinal), out string? canonical)
                ? canonical
                : genre;
    }
}
