using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>Outcome of listing playlist names parsed from a stored upload's Rekordbox XML.</summary>
    public sealed class ListUploadPlaylistsResult
    {
        public enum ListOutcome
        {
            Found,
            UploadNotFound,
            ParseFailed,
        }

        public ListOutcome Outcome { get; init; }

        public IReadOnlyList<string> Playlists { get; init; } = Array.Empty<string>();

        public string? ErrorMessage { get; init; }
    }
}
