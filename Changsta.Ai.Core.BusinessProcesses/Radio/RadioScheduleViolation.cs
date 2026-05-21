namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    internal enum RadioScheduleRule
    {
        SlotCountMismatch,
        GenreMismatch,
        SameStationSameDayRepeat,
        SameHourCrossStationDuplicate,
    }

    internal sealed class RadioScheduleViolation
    {
        required internal string StationId { get; init; }

        required internal RadioScheduleRule Rule { get; init; }

        required internal string Description { get; init; }

        internal int? Hour { get; init; }

        internal string? MixId { get; init; }
    }
}
