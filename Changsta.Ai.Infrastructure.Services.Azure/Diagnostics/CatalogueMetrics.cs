using System.Diagnostics.Metrics;

namespace Changsta.Ai.Infrastructure.Services.Azure.Diagnostics
{
    /// <summary>
    /// Custom metrics for catalogue failure modes. The counters surface intermittent RSS/blob/AI
    /// failures that the 'safe' fallbacks would otherwise hide (the only other signal being reduced
    /// recommendation quality). Register <see cref="MeterName"/> with the OpenTelemetry metrics
    /// pipeline so these flow to Azure Monitor. See issue #55.
    /// </summary>
    public static class CatalogueMetrics
    {
        public const string MeterName = "Changsta.Ai.Catalogue";

        public static readonly Meter Meter = new Meter(MeterName);

        public static readonly Counter<long> RssFetchFailures =
            Meter.CreateCounter<long>("catalogue.rss_fetch_failures");

        public static readonly Counter<long> BlobReadFailures =
            Meter.CreateCounter<long>("catalogue.blob_read_failures");

        public static readonly Counter<long> BlobWriteFailures =
            Meter.CreateCounter<long>("catalogue.blob_write_failures");

        public static readonly Counter<long> EnrichedWeightsLoadFailures =
            Meter.CreateCounter<long>("catalogue.enriched_weights_load_failures");

        public static readonly Counter<long> AiEnrichmentFailures =
            Meter.CreateCounter<long>("catalogue.ai_enrichment_failures");
    }
}
