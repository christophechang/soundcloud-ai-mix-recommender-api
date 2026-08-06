namespace Changsta.Ai.Core.Domain.MixLab
{
    /// <summary>
    /// Lifecycle of a MixLab library-map job (<c>maps/index.json</c> entry). Serialises to the
    /// lower-case strings <c>queued|running|succeeded|failed</c> via the camelCase
    /// <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/> configured in
    /// <see cref="Changsta.Ai.Infrastructure.Services.Azure.MixLab.MixLabJsonOptions"/>.
    /// </summary>
    public enum MixLabMapStatus
    {
        Queued,
        Running,
        Succeeded,
        Failed,
    }
}
