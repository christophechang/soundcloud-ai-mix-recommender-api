using System;
using System.Collections.Generic;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Infrastructure.Services.Azure.Models
{
    public sealed class MixCatalogDocument
    {
        public int SchemaVersion { get; init; } = 1;

        public DateTimeOffset LastUpdatedAt { get; init; }

        public IReadOnlyList<Mix> Mixes { get; init; } = Array.Empty<Mix>();
    }
}
