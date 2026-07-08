using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Enforces the fail-state discipline the repository does not (the repository's
    /// <c>FailAsync</c> is state-agnostic): only a running run may be failed, failing an
    /// already-failed run is an idempotent no-op, and any other state is a conflict. The log tail
    /// is truncated to 8 KB and folded into the stored error, since the manifest carries no
    /// separate log field. See issue #130.
    /// </summary>
    public sealed class FailMixLabRunUseCase : IFailMixLabRunUseCase
    {
        private const int MaxLogTailBytes = 8 * 1024;

        private const string LogTailSeparator = "\n\n--- log tail (truncated to 8 KB) ---\n";

        private readonly IMixLabRunRepository _runs;
        private readonly ILogger<FailMixLabRunUseCase> _logger;

        public FailMixLabRunUseCase(IMixLabRunRepository runs, ILogger<FailMixLabRunUseCase> logger)
        {
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<FailMixLabRunResult> FailAsync(
            string runId,
            string error,
            string? logTail,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);
            ArgumentException.ThrowIfNullOrWhiteSpace(error);

            MixLabRun? run = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false);
            if (run is null)
            {
                return new FailMixLabRunResult { Outcome = FailMixLabRunResult.FailOutcome.NotFound };
            }

            if (run.Status == MixLabRunStatus.Failed)
            {
                return new FailMixLabRunResult { Outcome = FailMixLabRunResult.FailOutcome.AlreadyFailed };
            }

            if (run.Status != MixLabRunStatus.Running)
            {
                return new FailMixLabRunResult { Outcome = FailMixLabRunResult.FailOutcome.Conflict };
            }

            string storedError = ComposeError(error, logTail);
            await _runs.FailAsync(runId, storedError, cancellationToken).ConfigureAwait(false);

            _logger.LogWarning("Failed MixLab run {RunId}.", runId);

            return new FailMixLabRunResult { Outcome = FailMixLabRunResult.FailOutcome.Failed };
        }

        private static string ComposeError(string error, string? logTail)
        {
            if (string.IsNullOrEmpty(logTail))
            {
                return error;
            }

            return error + LogTailSeparator + TruncateUtf8(logTail, MaxLogTailBytes);
        }

        private static string TruncateUtf8(string value, int maxBytes)
        {
            if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
            {
                return value;
            }

            var builder = new StringBuilder();
            int total = 0;
            foreach (Rune rune in value.EnumerateRunes())
            {
                int runeBytes = rune.Utf8SequenceLength;
                if (total + runeBytes > maxBytes)
                {
                    break;
                }

                total += runeBytes;
                builder.Append(rune.ToString());
            }

            return builder.ToString();
        }
    }
}
