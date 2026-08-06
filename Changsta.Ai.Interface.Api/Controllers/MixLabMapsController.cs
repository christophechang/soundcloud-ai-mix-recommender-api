using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;
using Changsta.Ai.Interface.Api.Errors;
using Changsta.Ai.Interface.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Changsta.Ai.Interface.Api.Controllers
{
    /// <summary>
    /// MixLab library-map job lifecycle: request (or refresh) a map for an upload, let a worker
    /// claim/complete/fail it, and read the result back. Transport only: request parsing and
    /// status-code mapping live here; all orchestration and validation live in the use cases,
    /// mirroring <see cref="MixLabRunsController"/>. One exception: <see cref="ICompleteMixLabMapUseCase"/>
    /// has no "invalid JSON" outcome, because the engine payload is stored verbatim and never
    /// deserialised by the use case, so the well-formedness check that <see cref="GetMapAsync"/>
    /// otherwise depends on (embedding the stored payload via <see cref="JsonDocument.Parse(byte[])"/>)
    /// is done here, at push time, instead. The whole controller is guarded by the shared MixLab
    /// bearer secret. See issue #128.
    /// </summary>
    [ApiController]
    [Route("api/mixlab")]
    [BearerSecret("MixLab:ApiSecret")]
    public sealed class MixLabMapsController : ControllerBase
    {
        // Map jobs must serialise with camelCase property names AND lower-case string enums
        // (status = "queued", not 0), exactly like MixLabRunsController's run manifests, so the
        // Python worker and the web UI parse them.
        private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        private readonly IRequestMixLabMapUseCase _request;
        private readonly IClaimMixLabMapUseCase _claim;
        private readonly ICompleteMixLabMapUseCase _complete;
        private readonly IFailMixLabMapUseCase _fail;
        private readonly IGetMixLabMapUseCase _get;

        public MixLabMapsController(
            IRequestMixLabMapUseCase request,
            IClaimMixLabMapUseCase claim,
            ICompleteMixLabMapUseCase complete,
            IFailMixLabMapUseCase fail,
            IGetMixLabMapUseCase get)
        {
            _request = request ?? throw new ArgumentNullException(nameof(request));
            _claim = claim ?? throw new ArgumentNullException(nameof(claim));
            _complete = complete ?? throw new ArgumentNullException(nameof(complete));
            _fail = fail ?? throw new ArgumentNullException(nameof(fail));
            _get = get ?? throw new ArgumentNullException(nameof(get));
        }

        [HttpPost("maps")]
        public async Task<IActionResult> RequestMapAsync([FromBody] JsonElement body, CancellationToken cancellationToken)
        {
            string? uploadId = body.ValueKind == JsonValueKind.Object
                && body.TryGetProperty("uploadId", out JsonElement uploadElement)
                && uploadElement.ValueKind == JsonValueKind.String
                ? uploadElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(uploadId))
            {
                return ApiProblem.BadRequest("uploadId is required.");
            }

            RequestMixLabMapResult result = await _request.RequestAsync(uploadId, cancellationToken).ConfigureAwait(false);

            return result.Outcome switch
            {
                RequestMixLabMapResult.RequestOutcome.Accepted =>
                    new JsonResult(result.Job) { StatusCode = StatusCodes.Status202Accepted, SerializerSettings = ManifestJsonOptions },
                RequestMixLabMapResult.RequestOutcome.UnknownUpload =>
                    ApiProblem.NotFound($"No MixLab upload found with id '{uploadId}'."),
                RequestMixLabMapResult.RequestOutcome.NoUploadsAvailable =>
                    ApiProblem.NotFound("No MixLab uploads are available yet."),
                _ => ApiProblem.Status(StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
            };
        }

        [HttpPost("maps/claim")]
        public async Task<IActionResult> ClaimAsync([FromBody] JsonElement body, CancellationToken cancellationToken)
        {
            // Mirrors MixLabRunsController.ClaimAsync exactly: workerId presence/shape validation
            // for the map claim action lives here, not in the use case.
            string? workerId = body.ValueKind == JsonValueKind.Object
                && body.TryGetProperty("workerId", out JsonElement workerElement)
                && workerElement.ValueKind == JsonValueKind.String
                ? workerElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(workerId))
            {
                return ApiProblem.BadRequest("workerId is required.");
            }

            MixLabMapJob? claimed = await _claim.ClaimAsync(workerId, cancellationToken).ConfigureAwait(false);

            if (claimed is null)
            {
                return NoContent();
            }

            return new JsonResult(claimed) { SerializerSettings = ManifestJsonOptions };
        }

        [HttpPost("maps/{uploadId}/result")]
        [RequestSizeLimit(32 * 1024 * 1024)]
        public async Task<IActionResult> ResultAsync([FromRoute] string uploadId, CancellationToken cancellationToken)
        {
            byte[] payload = await ReadBodyBytesAsync(cancellationToken).ConfigureAwait(false);

            // The payload is stored verbatim and never deserialised by CompleteMixLabMapUseCase, so
            // nothing else on the write path validates it is JSON at all. Reject unparseable bodies
            // here instead of letting a bad payload surface later as an unhandled exception from
            // GetMapAsync's JsonDocument.Parse when a client reads it back.
            if (!IsWellFormedJson(payload))
            {
                return ApiProblem.BadRequest("Result payload must be valid JSON.");
            }

            bool completed = await _complete.CompleteAsync(uploadId, payload, cancellationToken).ConfigureAwait(false);

            return completed
                ? NoContent()
                : ApiProblem.NotFound($"No running MixLab map job found for upload '{uploadId}'.");
        }

        [HttpPost("maps/{uploadId}/fail")]
        public async Task<IActionResult> FailAsync(
            [FromRoute] string uploadId,
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
                return ApiProblem.BadRequest("error is required.");
            }

            bool failed = await _fail.FailAsync(uploadId, error, cancellationToken).ConfigureAwait(false);

            return failed
                ? NoContent()
                : ApiProblem.NotFound($"No running MixLab map job found for upload '{uploadId}'.");
        }

        [HttpGet("maps/{uploadId}")]
        public async Task<IActionResult> GetMapAsync([FromRoute] string uploadId, CancellationToken cancellationToken)
        {
            GetMixLabMapResult result = await _get.GetAsync(uploadId, cancellationToken).ConfigureAwait(false);

            if (result.Outcome == GetMixLabMapResult.GetOutcome.NotFound)
            {
                return ApiProblem.NotFound($"No MixLab map job found for upload '{uploadId}'.");
            }

            // The engine payload is stored verbatim; embed it as nested, unescaped JSON (not a
            // JSON-encoded string) by cloning the parsed root element into the response object.
            // ResultAsync already rejected non-JSON payloads at push time, so this parse cannot
            // throw. Payload is null unless the job has succeeded (GetMixLabMapUseCase), and it can
            // also come back null for a succeeded job whose blob is missing — either way this must
            // serialise as payload: null, never crash.
            JsonElement? payload = result.Payload is null
                ? null
                : JsonDocument.Parse(result.Payload).RootElement.Clone();

            return new JsonResult(new { job = result.Job, payload }) { SerializerSettings = ManifestJsonOptions };
        }

        private static bool IsWellFormedJson(byte[] payload)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(payload);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private async Task<byte[]> ReadBodyBytesAsync(CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await Request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer.ToArray();
        }
    }
}
