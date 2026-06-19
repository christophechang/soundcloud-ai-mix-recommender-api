using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.Contracts.Catalogue
{
    public interface IBlobMixCatalogueRepository
    {
        Task<CatalogReadResult> ReadAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Writes the catalogue using optimistic concurrency. When <paramref name="expectedETag"/>
        /// is non-null the write is conditioned on <c>If-Match</c>; when null the write is
        /// create-only (<c>If-None-Match: *</c>). Throws
        /// <see cref="Changsta.Ai.Core.Exceptions.CatalogConcurrencyException"/> when the blob was
        /// modified concurrently. See issue #34.
        /// </summary>
        Task WriteAsync(IReadOnlyList<Mix> mixes, string? expectedETag, CancellationToken cancellationToken);
    }
}
