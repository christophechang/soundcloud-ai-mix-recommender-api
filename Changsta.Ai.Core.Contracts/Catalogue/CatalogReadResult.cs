using System.Collections.Generic;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.Contracts.Catalogue
{
    /// <summary>
    /// The result of reading the blob catalogue: the mixes plus the storage ETag captured at read
    /// time. The ETag is an opaque token passed back to <see cref="IBlobMixCatalogueRepository.WriteAsync"/>
    /// for optimistic concurrency (If-Match). It is <see langword="null"/> when the blob does not yet
    /// exist, signalling a first/create-only write. See issue #34.
    /// </summary>
    public sealed class CatalogReadResult
    {
        public CatalogReadResult(IReadOnlyList<Mix> mixes, string? eTag)
        {
            Mixes = mixes;
            ETag = eTag;
        }

        public IReadOnlyList<Mix> Mixes { get; }

        public string? ETag { get; }
    }
}
