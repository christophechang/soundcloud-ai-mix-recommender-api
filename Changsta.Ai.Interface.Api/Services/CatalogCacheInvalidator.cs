using System.Threading;
using Changsta.Ai.Core.Contracts.Catalogue;

namespace Changsta.Ai.Interface.Api.Services
{
    internal sealed class CatalogCacheInvalidator : ICatalogCacheInvalidator
    {
        private int _version;

        public int Version => _version;

        public void Invalidate() => Interlocked.Increment(ref _version);
    }
}
