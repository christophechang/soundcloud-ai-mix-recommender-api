using System;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.BusinessProcesses.NowSpinning
{
    internal static class SlotScorer
    {
        internal static double Score(Mix mix, SlotConfig config, int bpmTarget)
        {
            int? bpm = ComputeBpm(mix);
            if (bpm is null)
            {
                return 0.0;
            }

            double warmth = mix.Warmth ?? 0.0;

            double bpmScore = Math.Max(0, 8.0 - (Math.Abs(bpm.Value - bpmTarget) / 6.0));
            double warmthScore = Math.Max(0, 4.0 - (Math.Abs(warmth - config.WarmthTarget) / 0.25));
            double energyScore = EnergyMatches(mix.Energy, config.EnergyValues) ? 5.0 : 0.0;

            return bpmScore + warmthScore + energyScore;
        }

        internal static int? ComputeBpm(Mix mix)
        {
            if (mix.BpmMin.HasValue && mix.BpmMax.HasValue)
            {
                return (int)Math.Round((mix.BpmMin.Value + mix.BpmMax.Value) / 2.0);
            }

            return mix.BpmMin ?? mix.BpmMax;
        }

        private static bool EnergyMatches(string? energy, string[] energyValues)
        {
            if (energy is null)
            {
                return false;
            }

            foreach (string e in energyValues)
            {
                if (string.Equals(e, energy, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
