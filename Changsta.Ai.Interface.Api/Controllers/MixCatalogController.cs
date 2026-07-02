using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Normalization;
using Changsta.Ai.Interface.Api.Catalog;
using Changsta.Ai.Interface.Api.RateLimiting;
using Changsta.Ai.Interface.Api.Security;
using Changsta.Ai.Interface.Api.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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

        public MixCatalogController(
            IMixCatalogueProvider catalogueProvider,
            ICatalogFlushUseCase flushUseCase,
            IDeleteMixUseCase deleteMixUseCase)
        {
            _catalogueProvider = catalogueProvider;
            _flushUseCase = flushUseCase;
            _deleteMixUseCase = deleteMixUseCase;
        }

        [HttpPost("flush")]
        [EnableRateLimiting(RateLimitPolicies.Privileged)]
        [BearerSecret("Catalog:FlushSecret")]
        public async Task<IActionResult> FlushCatalogAsync(CancellationToken cancellationToken)
        {
            await _flushUseCase.FlushAsync(cancellationToken).ConfigureAwait(false);
            return Ok(new { flushed = true });
        }

        [HttpDelete("mixes")]
        [EnableRateLimiting(RateLimitPolicies.Privileged)]
        [BearerSecret("Catalog:FlushSecret")]
        public async Task<IActionResult> DeleteMixAsync(
            [FromQuery] string slug,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return BadRequest(new { error = "slug is required." });
            }

            bool deleted = await _deleteMixUseCase.DeleteAsync(slug, cancellationToken).ConfigureAwait(false);

            if (!deleted)
            {
                return NotFound(new { error = $"No mix found with slug '{slug}'." });
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
            if (ValidatePaging(page, pageSize) is IActionResult invalid)
            {
                return invalid;
            }

            IReadOnlyList<Mix> mixes = await LoadCatalogueAsync(CatalogMaxItems, cancellationToken).ConfigureAwait(false);
            return Ok(BuildPage(CatalogProjections.GenreTree(mixes, genre), page, pageSize));
        }

        [HttpGet("genres")]
        public async Task<IActionResult> GetGenresAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Mix> mixes = await LoadCatalogueAsync(CatalogMaxItems, cancellationToken).ConfigureAwait(false);
            return Ok(new GenresResponse { Genres = CatalogProjections.GenreNames(mixes) });
        }

        [HttpGet("mixes")]
        public async Task<IActionResult> GetMixesAsync(
            [FromQuery] string? genre,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            if (ValidatePaging(page, pageSize) is IActionResult invalid)
            {
                return invalid;
            }

            IReadOnlyList<Mix> mixes = await LoadCatalogueAsync(CatalogMaxItems, cancellationToken).ConfigureAwait(false);
            Mix[] ordered = CatalogProjections.MixesOrdered(mixes, genre);

            Response.Headers.Append("X-Total-Count", ordered.Length.ToString(CultureInfo.InvariantCulture));

            return Ok(BuildPage(ordered, page, pageSize));
        }

        [HttpGet("artists")]
        public async Task<IActionResult> GetArtistsAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Mix> mixes = await LoadCatalogueAsync(CatalogMaxItems, cancellationToken).ConfigureAwait(false);
            return Ok(new ArtistNamesResponse { Artists = CatalogProjections.ArtistNames(mixes) });
        }

        [HttpGet("tracks")]
        public async Task<IActionResult> GetTracksAsync(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            if (ValidatePaging(page, pageSize) is IActionResult invalid)
            {
                return invalid;
            }

            IReadOnlyList<Mix> mixes = await LoadCatalogueAsync(CatalogMaxItems, cancellationToken).ConfigureAwait(false);
            return Ok(BuildPage(CatalogProjections.TrackSummaries(mixes), page, pageSize));
        }

        [HttpGet("mixes/titles")]
        public async Task<IActionResult> GetMixTitlesAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Mix> mixes = await LoadCatalogueAsync(CatalogMaxItems, cancellationToken).ConfigureAwait(false);

            Response.Headers.Append("Cache-Control", "max-age=3600, public");

            return Ok(CatalogProjections.MixTitles(mixes));
        }

        [HttpGet("compass")]
        public async Task<IActionResult> GetCompassAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Mix> mixes = await LoadCatalogueAsync(CatalogMaxItems, cancellationToken).ConfigureAwait(false);

            Response.Headers.Append("Cache-Control", "max-age=3600, public");

            return Ok(CatalogProjections.Compass(mixes));
        }

        [HttpGet("mixes/{slug}")]
        public async Task<IActionResult> GetMixBySlugAsync(
            [FromRoute] string slug,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Mix> mixes = await _catalogueProvider
                .GetLatestAsync(int.MaxValue, cancellationToken)
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
        [HttpGet("artists/{*name}")]
        public async Task<IActionResult> GetMixesByArtistAsync(
            [FromRoute] string name,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            if (ValidatePaging(page, pageSize) is IActionResult invalid)
            {
                return invalid;
            }

            string artistName = NormalizeArtistRouteName(name);
            IReadOnlyList<Mix> mixes = await LoadCatalogueAsync(CatalogMaxItems, cancellationToken).ConfigureAwait(false);

            Mix[] results = mixes
                .Where(m => m.Tracklist.Any(t =>
                    string.Equals(t.Artist, artistName, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            if (results.Length == 0)
            {
                return NotFound(new { error = $"No mixes found for artist '{artistName}'." });
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

        private static string NormalizeArtistRouteName(string name)
        {
            const string MixesSuffix = "/mixes";

            return name.EndsWith(MixesSuffix, StringComparison.OrdinalIgnoreCase)
                ? name[..^MixesSuffix.Length]
                : name;
        }

        private async Task<IReadOnlyList<Mix>> LoadCatalogueAsync(int maxItems, CancellationToken cancellationToken) =>
            await _catalogueProvider.GetLatestAsync(maxItems, cancellationToken).ConfigureAwait(false);

        private IActionResult? ValidatePaging(int page, int pageSize) =>
            page < 1 || pageSize < 1 || pageSize > MaxPageSize
                ? BadRequest(new { error = $"page must be >= 1 and pageSize must be between 1 and {MaxPageSize}." })
                : null;
    }
}
