using System.Collections.Generic;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    internal sealed class RadioScoringContext
    {
        internal IReadOnlyList<string> RecentGenres { get; init; } = System.Array.Empty<string>();

        internal IReadOnlyList<string> RecentArtists { get; init; } = System.Array.Empty<string>();

        internal IReadOnlySet<string> CrossScheduleUsedIds { get; init; }
            = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
    }
}
