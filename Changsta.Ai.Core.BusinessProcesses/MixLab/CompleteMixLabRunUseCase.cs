using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Orchestrates run completion: state gate (idempotent for succeeded, 409 for any non-running
    /// state, 404 for unknown), immutable artifact writes, and folding the summary's concepts into
    /// the manifest via the repository's <c>CompleteAsync</c> (which owns the succeeded transition
    /// and index update). See issue #130.
    /// </summary>
    public sealed class CompleteMixLabRunUseCase : ICompleteMixLabRunUseCase
    {
        private const string SummaryArtifact = "summary.json";

        private const string ReportArtifact = "report.html";

        private const string ExportArtifact = "export.xml";

        private readonly IMixLabRunRepository _runs;
        private readonly IMixLabArtifactStore _artifacts;
        private readonly ILogger<CompleteMixLabRunUseCase> _logger;

        public CompleteMixLabRunUseCase(
            IMixLabRunRepository runs,
            IMixLabArtifactStore artifacts,
            ILogger<CompleteMixLabRunUseCase> logger)
        {
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
            _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<CompleteMixLabRunResult> CompleteAsync(
            string runId,
            Stream summary,
            Stream report,
            Stream? export,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);
            ArgumentNullException.ThrowIfNull(summary);
            ArgumentNullException.ThrowIfNull(report);

            MixLabRun? run = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false);
            if (run is null)
            {
                return Outcome(CompleteMixLabRunResult.CompleteOutcome.NotFound);
            }

            if (run.Status == MixLabRunStatus.Succeeded)
            {
                // Idempotent: do not rewrite the immutable artifacts or the manifest.
                return Outcome(CompleteMixLabRunResult.CompleteOutcome.AlreadyCompleted);
            }

            if (run.Status != MixLabRunStatus.Running)
            {
                return Outcome(CompleteMixLabRunResult.CompleteOutcome.Conflict);
            }

            byte[] summaryBytes = await BufferAsync(summary, cancellationToken).ConfigureAwait(false);

            if (!TryExtractConcepts(summaryBytes, out IReadOnlyList<MixLabRunConcept> concepts))
            {
                return new CompleteMixLabRunResult
                {
                    Outcome = CompleteMixLabRunResult.CompleteOutcome.InvalidSummary,
                    ErrorMessage = "summary is not valid JSON.",
                };
            }

            using (var summaryStream = new MemoryStream(summaryBytes, writable: false))
            {
                await _artifacts.SaveAsync(runId, SummaryArtifact, summaryStream, cancellationToken).ConfigureAwait(false);
            }

            await _artifacts.SaveAsync(runId, ReportArtifact, report, cancellationToken).ConfigureAwait(false);

            if (export is not null)
            {
                await _artifacts.SaveAsync(runId, ExportArtifact, export, cancellationToken).ConfigureAwait(false);
            }

            await _runs.CompleteAsync(runId, concepts, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Completed MixLab run {RunId} with {ConceptCount} concept(s); export {ExportState}.",
                runId,
                concepts.Count,
                export is null ? "absent" : "stored");

            return Outcome(CompleteMixLabRunResult.CompleteOutcome.Completed);
        }

        private static CompleteMixLabRunResult Outcome(CompleteMixLabRunResult.CompleteOutcome outcome)
        {
            return new CompleteMixLabRunResult { Outcome = outcome };
        }

        private static async Task<byte[]> BufferAsync(Stream stream, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer.ToArray();
        }

        private static bool TryExtractConcepts(byte[] summaryBytes, out IReadOnlyList<MixLabRunConcept> concepts)
        {
            concepts = Array.Empty<MixLabRunConcept>();

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(summaryBytes);
            }
            catch (JsonException)
            {
                return false;
            }

            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("concepts", out JsonElement conceptsElement)
                    || conceptsElement.ValueKind != JsonValueKind.Array)
                {
                    return true;
                }

                var extracted = new List<MixLabRunConcept>();
                foreach (JsonElement element in conceptsElement.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    string? conceptId = ReadString(element, "conceptId");
                    string? title = ReadString(element, "title");
                    if (string.IsNullOrWhiteSpace(conceptId) || string.IsNullOrWhiteSpace(title))
                    {
                        continue;
                    }

                    extracted.Add(new MixLabRunConcept { ConceptId = conceptId, Title = title });
                }

                concepts = extracted;
                return true;
            }
        }

        private static string? ReadString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
    }
}
