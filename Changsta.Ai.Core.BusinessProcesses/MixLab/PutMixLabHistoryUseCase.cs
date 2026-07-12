using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Exceptions;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Writes <c>history/concept-history.json</c>. The document is opaque (it mirrors the Python
    /// engine's history schema), so the only validation performed here is that the body parses as
    /// JSON; <see cref="IMixLabHistoryStore.PutAsync"/> owns the If-Match/create-only concurrency
    /// rule, whose rejection is mapped to <see cref="PutMixLabHistoryResult.PutOutcome.PreconditionFailed"/>.
    /// See docs/architecture/mixlab-anywhere.md §4 row 13 and issue #131.
    /// </summary>
    public sealed class PutMixLabHistoryUseCase : IPutMixLabHistoryUseCase
    {
        private readonly IMixLabHistoryStore _history;

        public PutMixLabHistoryUseCase(IMixLabHistoryStore history)
        {
            _history = history ?? throw new ArgumentNullException(nameof(history));
        }

        public async Task<PutMixLabHistoryResult> PutAsync(string content, string? ifMatchETag, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(content);

            if (!IsWellFormedJson(content))
            {
                return new PutMixLabHistoryResult
                {
                    Outcome = PutMixLabHistoryResult.PutOutcome.InvalidJson,
                    ErrorMessage = "Request body must be well-formed JSON.",
                };
            }

            try
            {
                string etag = await _history.PutAsync(content, ifMatchETag, cancellationToken).ConfigureAwait(false);
                return new PutMixLabHistoryResult { Outcome = PutMixLabHistoryResult.PutOutcome.Written, ETag = etag };
            }
            catch (MixLabConcurrencyException ex)
            {
                return new PutMixLabHistoryResult
                {
                    Outcome = PutMixLabHistoryResult.PutOutcome.PreconditionFailed,
                    ErrorMessage = ex.Message,
                };
            }
        }

        private static bool IsWellFormedJson(string content)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(content);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
