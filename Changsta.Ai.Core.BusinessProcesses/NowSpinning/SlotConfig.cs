namespace Changsta.Ai.Core.BusinessProcesses.NowSpinning
{
    internal sealed class SlotConfig
    {
        internal SlotConfig(SlotKey key, string label, int baseBpmTarget, double warmthTarget, string[] energyValues)
        {
            Key = key;
            Label = label;
            BaseBpmTarget = baseBpmTarget;
            WarmthTarget = warmthTarget;
            EnergyValues = energyValues;
        }

        internal SlotKey Key { get; }

        internal string Label { get; }

        internal int BaseBpmTarget { get; }

        internal double WarmthTarget { get; }

        internal string[] EnergyValues { get; }
    }
}
