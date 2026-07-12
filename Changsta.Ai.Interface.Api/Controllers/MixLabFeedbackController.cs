using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
    /// MixLab Anywhere field feedback: recording a verdict/rating/notes/publishedMixSlug against a
    /// completed run's concept, and the pending-queue read/ack pair the worker uses to fold that
    /// feedback into engine history. Transport only: request parsing and status-code mapping live
    /// here; all validation and orchestration live in the use cases. The whole controller is guarded
    /// by the shared MixLab bearer secret. See docs/architecture/mixlab-anywhere.md §4 rows 14-15,
    /// §5.3, and issue #131.
    /// </summary>
    [ApiController]
    [Route("api/mixlab")]
    [BearerSecret("MixLab:ApiSecret")]
    public sealed class MixLabFeedbackController : ControllerBase
    {
        private readonly ISubmitMixLabConceptFeedbackUseCase _submitFeedback;
        private readonly IGetPendingMixLabFeedbackUseCase _getPendingFeedback;
        private readonly IAckMixLabFeedbackUseCase _ackFeedback;

        public MixLabFeedbackController(
            ISubmitMixLabConceptFeedbackUseCase submitFeedback,
            IGetPendingMixLabFeedbackUseCase getPendingFeedback,
            IAckMixLabFeedbackUseCase ackFeedback)
        {
            _submitFeedback = submitFeedback ?? throw new ArgumentNullException(nameof(submitFeedback));
            _getPendingFeedback = getPendingFeedback ?? throw new ArgumentNullException(nameof(getPendingFeedback));
            _ackFeedback = ackFeedback ?? throw new ArgumentNullException(nameof(ackFeedback));
        }

        [HttpPost("runs/{id}/concepts/{conceptId}/feedback")]
        public async Task<IActionResult> SubmitFeedbackAsync(
            [FromRoute] string id,
            [FromRoute] string conceptId,
            [FromBody] JsonElement body,
            CancellationToken cancellationToken)
        {
            if (body.ValueKind != JsonValueKind.Object)
            {
                return BadRequest(new { error = "Request body must be a JSON object." });
            }

            string? verdict = ReadOptionalString(body, "verdict");
            int? rating = ReadOptionalInt(body, "rating");
            string? notes = ReadOptionalString(body, "notes");
            string? publishedMixSlug = ReadOptionalString(body, "publishedMixSlug");

            SubmitMixLabConceptFeedbackResult result = await _submitFeedback
                .SubmitAsync(id, conceptId, verdict, rating, notes, publishedMixSlug, cancellationToken)
                .ConfigureAwait(false);

            return result.Outcome switch
            {
                SubmitMixLabConceptFeedbackResult.SubmitOutcome.Recorded => Ok(new { runId = id, conceptId }),
                SubmitMixLabConceptFeedbackResult.SubmitOutcome.InvalidRequest =>
                    BadRequest(new { error = result.ErrorMessage }),
                SubmitMixLabConceptFeedbackResult.SubmitOutcome.RunNotFound =>
                    NotFound(new { error = $"Run '{id}' not found." }),
                SubmitMixLabConceptFeedbackResult.SubmitOutcome.ConceptNotFound =>
                    NotFound(new { error = $"Concept '{conceptId}' not found on run '{id}'." }),
                _ => StatusCode(StatusCodes.Status500InternalServerError),
            };
        }

        [HttpGet("feedback/pending")]
        public async Task<IActionResult> GetPendingFeedbackAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<MixLabFeedbackEvent> pending = await _getPendingFeedback
                .GetPendingAsync(cancellationToken)
                .ConfigureAwait(false);

            var view = pending.Select(ToView).ToArray();

            return Ok(view);
        }

        [HttpPost("feedback/ack")]
        public async Task<IActionResult> AckFeedbackAsync([FromBody] JsonElement body, CancellationToken cancellationToken)
        {
            if (!TryReadEventIds(body, out IReadOnlyList<string>? eventIds, out string? error))
            {
                return BadRequest(new { error });
            }

            await _ackFeedback.AckAsync(eventIds!, cancellationToken).ConfigureAwait(false);

            return Ok(new { acked = eventIds!.Count });
        }

        private static object ToView(MixLabFeedbackEvent feedbackEvent)
        {
            return new
            {
                eventId = feedbackEvent.EventId,
                runId = feedbackEvent.RunId,
                conceptId = feedbackEvent.ConceptId,
                verdict = feedbackEvent.Verdict is MixLabFeedbackVerdict verdict
                    ? MixLabFeedbackVerdictWireValues.ToWireValue(verdict)
                    : null,
                rating = feedbackEvent.Rating,
                notes = feedbackEvent.Notes,
                publishedMixSlug = feedbackEvent.PublishedMixSlug,
                recordedAt = feedbackEvent.RecordedAt,
            };
        }

        private static bool TryReadEventIds(JsonElement body, out IReadOnlyList<string>? eventIds, out string? error)
        {
            eventIds = null;
            error = null;

            if (body.ValueKind != JsonValueKind.Object
                || !body.TryGetProperty("eventIds", out JsonElement eventIdsElement)
                || eventIdsElement.ValueKind != JsonValueKind.Array)
            {
                error = "'eventIds' is required and must be an array of strings.";
                return false;
            }

            var ids = new List<string>();
            foreach (JsonElement item in eventIdsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    error = "'eventIds' must contain only strings.";
                    return false;
                }

                string? id = item.GetString();
                if (id is not null)
                {
                    ids.Add(id);
                }
            }

            eventIds = ids;
            return true;
        }

        private static string? ReadOptionalString(JsonElement body, string propertyName)
        {
            return body.TryGetProperty(propertyName, out JsonElement element) && element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;
        }

        private static int? ReadOptionalInt(JsonElement body, string propertyName)
        {
            return body.TryGetProperty(propertyName, out JsonElement element)
                && element.ValueKind == JsonValueKind.Number
                && element.TryGetInt32(out int value)
                ? value
                : null;
        }
    }
}
