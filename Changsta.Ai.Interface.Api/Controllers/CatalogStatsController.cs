using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Interface.Api.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Changsta.Ai.Interface.Api.Controllers
{
    /// <summary>
    /// Live SoundCloud play and like counts for the whole catalogue. The counts are never stored,
    /// so every response, 200 or 204, carries <c>Cache-Control: no-store</c>.
    /// </summary>
    [ApiController]
    [Route("api/catalog")]
    public sealed class CatalogStatsController : ControllerBase
    {
        private const string MixIdPrefix = "tag:soundcloud,2010:tracks/";

        private readonly IMixEngagementProvider _provider;

        public CatalogStatsController(IMixEngagementProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetStatsAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<MixEngagement>? stats = await _provider.GetAllAsync(cancellationToken).ConfigureAwait(false);

            Response.Headers.CacheControl = "no-store";

            if (stats is null)
            {
                return NoContent();
            }

            return Ok(new CatalogStatsResponse
            {
                Items = stats.Select(MapItem).ToArray(),
            });
        }

        private static MixStatsVm MapItem(MixEngagement engagement) =>
            new MixStatsVm
            {
                Id = MixIdPrefix + engagement.TrackId.ToString(CultureInfo.InvariantCulture),
                Plays = engagement.Plays,
                Likes = engagement.Likes,
            };
    }
}
