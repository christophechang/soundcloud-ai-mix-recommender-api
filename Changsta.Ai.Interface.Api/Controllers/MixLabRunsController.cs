using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;
using Changsta.Ai.Interface.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Changsta.Ai.Interface.Api.Controllers
{
    /// <summary>
    /// MixLab Anywhere run lifecycle and worker endpoints. Transport only: request parsing and
    /// status-code mapping live here; all orchestration and validation live in the use cases. The
    /// whole controller is guarded by the shared MixLab bearer secret. See issue #130.
    /// </summary>
    [ApiController]
    [Route("api/mixlab")]
    [BearerSecret("MixLab:ApiSecret")]
    public sealed class MixLabRunsController : ControllerBase
    {
        // Run manifests and index entries must serialise with camelCase property names AND
        // lower-case string enums (status = "queued", not 0) so the Python worker and the web UI
        // parse them. The global MVC options set camelCase names but not string enums, so serialise
        // these responses explicitly.
        private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        private readonly IEnqueueMixLabRunUseCase _enqueue;
        private readonly IClaimMixLabRunUseCase _claim;
        private readonly ICompleteMixLabRunUseCase _complete;
        private readonly IFailMixLabRunUseCase _fail;
        private readonly IMixLabRunQueryUseCase _query;
        private readonly IOpenMixLabRunArtifactUseCase _artifacts;

        public MixLabRunsController(
            IEnqueueMixLabRunUseCase enqueue,
            IClaimMixLabRunUseCase claim,
            ICompleteMixLabRunUseCase complete,
            IFailMixLabRunUseCase fail,
            IMixLabRunQueryUseCase query,
            IOpenMixLabRunArtifactUseCase artifacts)
        {
            _enqueue = enqueue;
            _claim = claim;
            _complete = complete;
            _fail = fail;
            _query = query;
            _artifacts = artifacts;
        }

        [HttpPost("runs")]
        public async Task<IActionResult> EnqueueRunAsync([FromBody] JsonElement body, CancellationToken cancellationToken)
        {
            if (body.ValueKind != JsonValueKind.Object)
            {
                return BadRequest(new { error = "Request body must be a JSON object." });
            }

            JsonElement flags = body.TryGetProperty("flags", out JsonElement flagsElement) ? flagsElement : default;
            string? uploadId = body.TryGetProperty("uploadId", out JsonElement uploadElement)
                && uploadElement.ValueKind == JsonValueKind.String
                ? uploadElement.GetString()
                : null;

            EnqueueMixLabRunResult result = await _enqueue
                .EnqueueAsync(flags, uploadId, cancellationToken)
                .ConfigureAwait(false);

            return result.Outcome switch
            {
                EnqueueMixLabRunResult.EnqueueOutcome.Created =>
                    StatusCode(StatusCodes.Status202Accepted, new { runId = result.RunId }),
                EnqueueMixLabRunResult.EnqueueOutcome.InvalidRequest =>
                    BadRequest(new { error = result.ErrorMessage }),
                EnqueueMixLabRunResult.EnqueueOutcome.NoUploadsAvailable =>
                    BadRequest(new { error = result.ErrorMessage }),
                EnqueueMixLabRunResult.EnqueueOutcome.UnknownUpload =>
                    NotFound(new { error = result.ErrorMessage }),
                _ => StatusCode(StatusCodes.Status500InternalServerError),
            };
        }

        [HttpPost("worker/claim")]
        public async Task<IActionResult> ClaimAsync([FromBody] JsonElement body, CancellationToken cancellationToken)
        {
            string? workerId = body.ValueKind == JsonValueKind.Object
                && body.TryGetProperty("workerId", out JsonElement workerElement)
                && workerElement.ValueKind == JsonValueKind.String
                ? workerElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(workerId))
            {
                return BadRequest(new { error = "workerId is required." });
            }

            MixLabRun? claimed = await _claim.ClaimAsync(workerId, cancellationToken).ConfigureAwait(false);

            if (claimed is null)
            {
                return NoContent();
            }

            return new JsonResult(claimed) { SerializerSettings = ManifestJsonOptions };
        }

        [HttpPost("runs/{id}/complete")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> CompleteRunAsync(
            [FromRoute] string id,
            [FromForm] IFormFile? summary,
            [FromForm] IFormFile? report,
            [FromForm] IFormFile? export,
            CancellationToken cancellationToken)
        {
            if (summary is null || report is null)
            {
                return BadRequest(new { error = "summary and report files are required." });
            }

            await using Stream summaryStream = summary.OpenReadStream();
            await using Stream reportStream = report.OpenReadStream();
            await using Stream? exportStream = export?.OpenReadStream();

            CompleteMixLabRunResult result = await _complete
                .CompleteAsync(id, summaryStream, reportStream, exportStream, cancellationToken)
                .ConfigureAwait(false);

            return result.Outcome switch
            {
                CompleteMixLabRunResult.CompleteOutcome.Completed =>
                    Ok(new { runId = id, status = "succeeded" }),
                CompleteMixLabRunResult.CompleteOutcome.AlreadyCompleted =>
                    Ok(new { runId = id, status = "succeeded" }),
                CompleteMixLabRunResult.CompleteOutcome.Conflict =>
                    Conflict(new { error = $"Run '{id}' is not running." }),
                CompleteMixLabRunResult.CompleteOutcome.NotFound =>
                    NotFound(new { error = $"Run '{id}' not found." }),
                CompleteMixLabRunResult.CompleteOutcome.InvalidSummary =>
                    BadRequest(new { error = result.ErrorMessage }),
                _ => StatusCode(StatusCodes.Status500InternalServerError),
            };
        }

        [HttpPost("runs/{id}/fail")]
        public async Task<IActionResult> FailRunAsync(
            [FromRoute] string id,
            [FromBody] JsonElement body,
            CancellationToken cancellationToken)
        {
            string? error = body.ValueKind == JsonValueKind.Object
                && body.TryGetProperty("error", out JsonElement errorElement)
                && errorElement.ValueKind == JsonValueKind.String
                ? errorElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(error))
            {
                return BadRequest(new { error = "error is required." });
            }

            string? logTail = body.TryGetProperty("logTail", out JsonElement logTailElement)
                && logTailElement.ValueKind == JsonValueKind.String
                ? logTailElement.GetString()
                : null;

            FailMixLabRunResult result = await _fail
                .FailAsync(id, error, logTail, cancellationToken)
                .ConfigureAwait(false);

            return result.Outcome switch
            {
                FailMixLabRunResult.FailOutcome.Failed => Ok(new { runId = id, status = "failed" }),
                FailMixLabRunResult.FailOutcome.AlreadyFailed => Ok(new { runId = id, status = "failed" }),
                FailMixLabRunResult.FailOutcome.Conflict =>
                    Conflict(new { error = $"Run '{id}' is not running." }),
                FailMixLabRunResult.FailOutcome.NotFound =>
                    NotFound(new { error = $"Run '{id}' not found." }),
                _ => StatusCode(StatusCodes.Status500InternalServerError),
            };
        }

        [HttpGet("runs")]
        public async Task<IActionResult> ListRunsAsync(
            [FromQuery] int? take,
            [FromQuery] int? skip,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<MixLabRunIndexEntry> entries = await _query
                .ListAsync(take, skip, cancellationToken)
                .ConfigureAwait(false);

            return new JsonResult(entries) { SerializerSettings = ManifestJsonOptions };
        }

        [HttpGet("runs/{id}")]
        public async Task<IActionResult> GetRunAsync([FromRoute] string id, CancellationToken cancellationToken)
        {
            MixLabRun? run = await _query.GetAsync(id, cancellationToken).ConfigureAwait(false);

            if (run is null)
            {
                return NotFound(new { error = $"Run '{id}' not found." });
            }

            return new JsonResult(run) { SerializerSettings = ManifestJsonOptions };
        }

        [HttpGet("runs/{id}/report")]
        public Task<IActionResult> GetReportAsync([FromRoute] string id, CancellationToken cancellationToken)
        {
            return OpenArtifactAsync(id, MixLabRunArtifactKind.Report, cancellationToken);
        }

        [HttpGet("runs/{id}/export")]
        public Task<IActionResult> GetExportAsync([FromRoute] string id, CancellationToken cancellationToken)
        {
            return OpenArtifactAsync(id, MixLabRunArtifactKind.Export, cancellationToken);
        }

        [HttpGet("runs/{id}/summary")]
        public Task<IActionResult> GetSummaryAsync([FromRoute] string id, CancellationToken cancellationToken)
        {
            return OpenArtifactAsync(id, MixLabRunArtifactKind.Summary, cancellationToken);
        }

        private async Task<IActionResult> OpenArtifactAsync(string id, MixLabRunArtifactKind kind, CancellationToken cancellationToken)
        {
            MixLabRunArtifactResult result = await _artifacts.OpenAsync(id, kind, cancellationToken).ConfigureAwait(false);

            return result.Status switch
            {
                MixLabRunArtifactResult.ArtifactStatus.Found => File(result.Content!, result.ContentType!),
                MixLabRunArtifactResult.ArtifactStatus.RunNotFound =>
                    NotFound(new { error = $"Run '{id}' not found." }),
                MixLabRunArtifactResult.ArtifactStatus.ArtifactNotFound when kind == MixLabRunArtifactKind.Export =>
                    NoContent(),
                MixLabRunArtifactResult.ArtifactStatus.ArtifactNotFound =>
                    NotFound(new { error = "Artifact not found." }),
                _ => StatusCode(StatusCodes.Status500InternalServerError),
            };
        }
    }
}
