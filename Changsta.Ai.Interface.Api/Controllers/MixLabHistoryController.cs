using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Interface.Api.Errors;
using Changsta.Ai.Interface.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Changsta.Ai.Interface.Api.Controllers
{
    /// <summary>
    /// MixLab Anywhere engine history mirror: the raw <c>history/concept-history.json</c> document
    /// the Mac mini worker and the API keep in sync. Transport only: request parsing and
    /// status-code mapping live here; the JSON well-formedness check and ETag concurrency mapping
    /// live in the use cases. The whole controller is guarded by the shared MixLab bearer secret.
    /// See docs/architecture/mixlab-anywhere.md §4 row 13 and issue #131.
    /// </summary>
    [ApiController]
    [Route("api/mixlab")]
    [BearerSecret("MixLab:ApiSecret")]
    public sealed class MixLabHistoryController : ControllerBase
    {
        private const string ETagHeaderName = "ETag";

        private const string IfMatchHeaderName = "If-Match";

        private const int HistoryRequestSizeLimitBytes = 8 * 1024 * 1024;

        private readonly IGetMixLabHistoryUseCase _getHistory;
        private readonly IPutMixLabHistoryUseCase _putHistory;

        public MixLabHistoryController(IGetMixLabHistoryUseCase getHistory, IPutMixLabHistoryUseCase putHistory)
        {
            _getHistory = getHistory ?? throw new ArgumentNullException(nameof(getHistory));
            _putHistory = putHistory ?? throw new ArgumentNullException(nameof(putHistory));
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetHistoryAsync(CancellationToken cancellationToken)
        {
            MixLabHistorySnapshot? snapshot = await _getHistory.GetAsync(cancellationToken).ConfigureAwait(false);

            if (snapshot is null)
            {
                return ApiProblem.NotFound("MixLab history has not been written yet.");
            }

            Response.Headers[ETagHeaderName] = snapshot.ETag;
            return Content(snapshot.Content, "application/json");
        }

        [HttpPut("history")]
        [RequestSizeLimit(HistoryRequestSizeLimitBytes)]
        public async Task<IActionResult> PutHistoryAsync(CancellationToken cancellationToken)
        {
            string content = await ReadBodyAsync(cancellationToken).ConfigureAwait(false);
            string? ifMatchETag = Request.Headers[IfMatchHeaderName];

            PutMixLabHistoryResult result = await _putHistory
                .PutAsync(content, ifMatchETag, cancellationToken)
                .ConfigureAwait(false);

            if (result.Outcome == PutMixLabHistoryResult.PutOutcome.Written)
            {
                Response.Headers[ETagHeaderName] = result.ETag !;
            }

            return result.Outcome switch
            {
                PutMixLabHistoryResult.PutOutcome.Written => Ok(new { etag = result.ETag }),
                PutMixLabHistoryResult.PutOutcome.InvalidJson => ApiProblem.BadRequest(result.ErrorMessage),
                PutMixLabHistoryResult.PutOutcome.PreconditionFailed =>
                    ApiProblem.Status(StatusCodes.Status412PreconditionFailed, result.ErrorMessage),
                _ => ApiProblem.Status(StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
            };
        }

        private async Task<string> ReadBodyAsync(CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await Request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            return Encoding.UTF8.GetString(buffer.ToArray());
        }
    }
}
