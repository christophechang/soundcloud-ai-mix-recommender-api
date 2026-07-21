using System.Collections.Generic;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    /// <summary>
    /// The hydrated catalogue plus a single "anything derived actually changed" flag. Collapsing
    /// the per-field flags here is deliberate: adding a new derived field no longer means
    /// threading another bool through the persistence decision, which is how a field gets
    /// silently left out of write-back.
    /// </summary>
    internal sealed class CatalogueHydrationResult
    {
        required public IReadOnlyList<Mix> Mixes { get; init; }

        required public bool Changed { get; init; }
    }
}
