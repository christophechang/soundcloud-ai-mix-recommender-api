namespace Changsta.Ai.Core.Domain.MixLab
{
    /// <summary>
    /// Lifecycle of a MixLab Anywhere run manifest (<c>runs/{runId}/run.json</c>). See
    /// docs/architecture/mixlab-anywhere.md §5.1. Serialises to the lower-case strings
    /// <c>queued|running|succeeded|failed</c> via the camelCase <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/>
    /// configured in <see cref="Changsta.Ai.Infrastructure.Services.Azure.MixLab.MixLabJsonOptions"/>.
    /// </summary>
    public enum MixLabRunStatus
    {
        Queued,
        Running,
        Succeeded,
        Failed,
    }
}
