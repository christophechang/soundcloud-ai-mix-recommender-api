namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    internal sealed class SlotConfig
    {
        internal SlotConfig(SlotKey key, string label, int bpmPercentile, double warmthTarget, string[] energyValues)
        {
            Key = key;
            Label = label;
            BpmPercentile = bpmPercentile;
            WarmthTarget = warmthTarget;
            EnergyValues = energyValues;
        }

        internal SlotKey Key { get; }

        internal string Label { get; }

        internal int BpmPercentile { get; }

        internal double WarmthTarget { get; }

        internal string[] EnergyValues { get; }
    }
}
