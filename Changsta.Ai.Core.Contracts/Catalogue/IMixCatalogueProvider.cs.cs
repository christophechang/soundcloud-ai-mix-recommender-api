using System;
using System.Collections.Generic;
using System.Text;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.Contracts.Catalogue
{
    public interface IMixCatalogueProvider
    {
        Task<IReadOnlyList<Mix>> GetLatestAsync(int maxItems, CancellationToken cancellationToken);
    }
}
