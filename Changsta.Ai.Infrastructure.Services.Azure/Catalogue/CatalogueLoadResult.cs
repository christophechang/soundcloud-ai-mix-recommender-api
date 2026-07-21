using System.Collections.Generic;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    /// <summary>
    /// The two sources a catalogue is built from, plus what reading them told us. <see cref="BlobReadSucceeded"/>
    /// gates write-back: a failed blob read must never be persisted over intact data.
    /// </summary>
    internal sealed class CatalogueLoadResult
    {
        required public IReadOnlyList<Mix> BlobMixes { get; init; }

        required public IReadOnlyList<Mix> RssMixes { get; init; }

        public string? BlobETag { get; init; }

        required public bool BlobReadSucceeded { get; init; }

        /// <summary>True when genre normalisation rewrote at least one blob mix.</summary>
        required public bool BlobGenresChanged { get; init; }
    }
}
