using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Changsta.Ai.Core.Contracts.MixLab;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Opens a stored upload via <see cref="IOpenUploadUseCase"/> (which resolves <c>latest</c> and
    /// returns null when absent) and parses its playlist paths. Malformed content yields a
    /// ParseFailed outcome rather than throwing to the caller.
    /// </summary>
    public sealed class ListUploadPlaylistsUseCase : IListUploadPlaylistsUseCase
    {
        private readonly IOpenUploadUseCase _openUpload;

        public ListUploadPlaylistsUseCase(IOpenUploadUseCase openUpload)
        {
            _openUpload = openUpload ?? throw new ArgumentNullException(nameof(openUpload));
        }

        public async Task<ListUploadPlaylistsResult> ListAsync(string uploadId, CancellationToken cancellationToken)
        {
            MixLabUploadContent? content = await _openUpload.OpenAsync(uploadId, cancellationToken).ConfigureAwait(false);
            if (content is null)
            {
                return new ListUploadPlaylistsResult
                {
                    Outcome = ListUploadPlaylistsResult.ListOutcome.UploadNotFound,
                    ErrorMessage = $"No MixLab upload found with id '{uploadId}'.",
                };
            }

            await using Stream stream = content.Content;
            try
            {
                IReadOnlyList<string> playlists = RekordboxPlaylistReader.ReadPlaylistPaths(stream);
                return new ListUploadPlaylistsResult
                {
                    Outcome = ListUploadPlaylistsResult.ListOutcome.Found,
                    Playlists = playlists,
                };
            }
            catch (Exception ex) when (ex is XmlException or InvalidDataException)
            {
                return new ListUploadPlaylistsResult
                {
                    Outcome = ListUploadPlaylistsResult.ListOutcome.ParseFailed,
                    ErrorMessage = "The stored collection could not be read as Rekordbox XML.",
                };
            }
        }
    }
}
