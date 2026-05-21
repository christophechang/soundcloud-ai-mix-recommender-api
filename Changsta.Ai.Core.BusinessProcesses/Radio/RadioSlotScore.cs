namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    internal sealed class RadioSlotScore
    {
        internal double Total { get; init; }

        internal double EnergyScore { get; init; }

        internal double WarmthScore { get; init; }

        internal double BpmScore { get; init; }

        internal double FreshnessBonus { get; init; }

        internal double GenreClusterPenalty { get; init; }

        internal double ArtistPenalty { get; init; }

        internal bool UnknownEnergy { get; init; }

        internal string? EnergyWarning { get; init; }
    }
}
