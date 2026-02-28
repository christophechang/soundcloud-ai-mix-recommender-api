using System.Collections.Generic;
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

        private readonly IMixCatalogueProvider _catalogueProvider;

        public MixCatalogController(IMixCatalogueProvider catalogueProvider)
        {
            _catalogueProvider = catalogueProvider;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<Mix>>> GetCatalogAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<Mix> mixes = await _catalogueProvider
                .GetLatestAsync(CatalogMaxItems, cancellationToken)
                .ConfigureAwait(false);

            return Ok(mixes);
        }
    }
}
