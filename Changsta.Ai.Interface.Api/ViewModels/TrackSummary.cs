namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class TrackSummary
    {
        required public string Artist { get; init; }

        required public string[] GenresSeen { get; init; }

        required public int RecurrenceCount { get; init; }

        required public string Title { get; init; }
    }
}
