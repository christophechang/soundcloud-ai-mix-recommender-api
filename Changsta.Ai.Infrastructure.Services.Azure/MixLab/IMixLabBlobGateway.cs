using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Infrastructure.Services.Azure.MixLab
{
    /// <summary>
    /// Narrow seam over blob primitives used by the MixLab repositories (see
    /// docs/architecture/mixlab-anywhere.md §3). Exists so the read-modify-write, claim, and
    /// idempotency logic in the repositories can be unit tested against an in-memory fake instead
    /// of the real Azure SDK — see <see cref="MixLabBlobGateway"/> for the production
    /// implementation and Changsta.Ai.Tests.Unit/MixLab/FakeMixLabBlobGateway for the fake. This
    /// mirrors how <c>Catalogue/BlobMixCatalogueRepository</c> is itself untested directly and
    /// only exercised through fakes of its own interface — issue #128.
    /// </summary>
    internal interface IMixLabBlobGateway
    {
        /// <summary>Returns <see langword="null"/> when the blob does not exist (404).</summary>
        Task<MixLabBlobReadResult?> ReadAsync(string blobPath, CancellationToken cancellationToken);

        /// <summary>
        /// Writes <paramref name="content"/> at <paramref name="blobPath"/>. When
        /// <paramref name="expectedETag"/> is non-null the write is conditioned on
        /// <c>If-Match</c>; when null the write is create-only (<c>If-None-Match: *</c>). Throws
        /// <see cref="Changsta.Ai.Core.Exceptions.MixLabConcurrencyException"/> on a precondition
        /// failure. Returns the new ETag.
        /// </summary>
        Task<string> WriteAsync(string blobPath, ReadOnlyMemory<byte> content, string? expectedETag, CancellationToken cancellationToken);

        /// <summary>Opens a lazily-read stream over an existing blob (for artifact/upload downloads).</summary>
        Task<Stream> OpenReadStreamAsync(string blobPath, CancellationToken cancellationToken);

        /// <summary>
        /// Writes an immutable artifact blob (create-only). Throws
        /// <see cref="Changsta.Ai.Core.Exceptions.MixLabConcurrencyException"/> if the blob already
        /// exists.
        /// </summary>
        Task WriteStreamAsync(string blobPath, Stream content, CancellationToken cancellationToken);

        Task<bool> ExistsAsync(string blobPath, CancellationToken cancellationToken);

        Task DeleteAsync(string blobPath, CancellationToken cancellationToken);
    }
}
