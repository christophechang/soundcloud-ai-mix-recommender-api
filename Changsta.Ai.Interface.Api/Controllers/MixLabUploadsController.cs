using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;
using Changsta.Ai.Interface.Api.Security;
using Microsoft.AspNetCore.Mvc;

namespace Changsta.Ai.Interface.Api.Controllers
{
    /// <summary>
    /// MixLab upload endpoints: store a Rekordbox collection XML, list the uploads index, and
    /// stream a stored upload back. See docs/architecture/mixlab-anywhere.md §4 rows 1-3 and issue
    /// #129.
    /// </summary>
    [ApiController]
    [Route("api/mixlab")]
    [Produces("application/json")]
    [BearerSecret("MixLab:ApiSecret")]
    public sealed class MixLabUploadsController : ControllerBase
    {
        private const string GzipContentEncoding = "gzip";

        private readonly IUploadCollectionUseCase _uploadCollectionUseCase;
        private readonly IGetUploadsUseCase _getUploadsUseCase;
        private readonly IOpenUploadUseCase _openUploadUseCase;
        private readonly IListUploadPlaylistsUseCase _listUploadPlaylistsUseCase;

        public MixLabUploadsController(
            IUploadCollectionUseCase uploadCollectionUseCase,
            IGetUploadsUseCase getUploadsUseCase,
            IOpenUploadUseCase openUploadUseCase,
            IListUploadPlaylistsUseCase listUploadPlaylistsUseCase)
        {
            _uploadCollectionUseCase = uploadCollectionUseCase ?? throw new ArgumentNullException(nameof(uploadCollectionUseCase));
            _getUploadsUseCase = getUploadsUseCase ?? throw new ArgumentNullException(nameof(getUploadsUseCase));
            _openUploadUseCase = openUploadUseCase ?? throw new ArgumentNullException(nameof(openUploadUseCase));
            _listUploadPlaylistsUseCase = listUploadPlaylistsUseCase ?? throw new ArgumentNullException(nameof(listUploadPlaylistsUseCase));
        }

        [HttpPost("uploads")]
        [RequestSizeLimit(64 * 1024 * 1024)]
        public async Task<IActionResult> UploadAsync(
            [FromQuery] string? label,
            CancellationToken cancellationToken)
        {
            // Content-Encoding is genuinely transport — the sniff/compress decision itself lives in
            // the use case so it can be unit tested without an HTTP pipeline.
            string? contentEncoding = Request.Headers["Content-Encoding"];
            bool contentEncodingSaysGzip = contentEncoding is not null
                && contentEncoding.Contains(GzipContentEncoding, StringComparison.OrdinalIgnoreCase);

            MixLabUpload upload = await _uploadCollectionUseCase
                .UploadAsync(Request.Body, contentEncodingSaysGzip, label, cancellationToken)
                .ConfigureAwait(false);

            return StatusCode(201, new { uploadId = upload.UploadId, sizeBytes = upload.SizeBytes });
        }

        [HttpGet("uploads")]
        public async Task<IActionResult> GetUploadsAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<MixLabUpload> uploads = await _getUploadsUseCase
                .GetUploadsAsync(cancellationToken)
                .ConfigureAwait(false);

            return Ok(uploads);
        }

        [HttpGet("uploads/{id}")]
        public async Task<IActionResult> GetUploadAsync(
            [FromRoute] string id,
            CancellationToken cancellationToken)
        {
            MixLabUploadContent? content = await _openUploadUseCase
                .OpenAsync(id, cancellationToken)
                .ConfigureAwait(false);

            if (content is null)
            {
                return NotFound(new { error = $"No MixLab upload found with id '{id}'." });
            }

            return File(content.Content, "application/gzip");
        }

        [HttpGet("uploads/{id}/playlists")]
        public async Task<IActionResult> GetUploadPlaylistsAsync(
            [FromRoute] string id,
            CancellationToken cancellationToken)
        {
            ListUploadPlaylistsResult result = await _listUploadPlaylistsUseCase
                .ListAsync(id, cancellationToken)
                .ConfigureAwait(false);

            return result.Outcome switch
            {
                ListUploadPlaylistsResult.ListOutcome.Found => Ok(result.Playlists),
                ListUploadPlaylistsResult.ListOutcome.UploadNotFound =>
                    NotFound(new { error = result.ErrorMessage }),
                _ => BadRequest(new { error = result.ErrorMessage }),
            };
        }
    }
}
