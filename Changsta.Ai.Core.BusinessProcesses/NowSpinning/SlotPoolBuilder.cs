using System;
using System.Collections.Generic;
using System.Linq;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;

namespace Changsta.Ai.Core.BusinessProcesses.NowSpinning
{
    internal static class SlotPoolBuilder
    {
        private const double MinAbsoluteScore = 3.0;
        private const double PercentileKeepFraction = 0.40;

        internal static NowSpinningPools Build(IReadOnlyList<Mix> mixes)
        {
            var pools = new Dictionary<(SlotKey, DayBucket), IReadOnlyList<PoolEntry>>();

            foreach (SlotKey slot in SlotDefinitions.SlotOrder)
            {
                SlotConfig config = SlotDefinitions.Slots[slot];

                foreach (DayBucket day in Enum.GetValues<DayBucket>())
                {
                    int bpmTarget = SlotDefinitions.GetBpmTarget(slot, day);

                    var scored = new List<(Mix mix, double score, int bpm)>();

                    foreach (Mix mix in mixes)
                    {
                        int? bpm = SlotScorer.ComputeBpm(mix);
                        if (bpm is null)
                        {
                            continue;
                        }

                        double score = SlotScorer.Score(mix, config, bpmTarget);
                        scored.Add((mix, score, bpm.Value));
                    }

                    if (scored.Count == 0)
                    {
                        pools[(slot, day)] = Array.Empty<PoolEntry>();
                        continue;
                    }

                    double threshold = ComputeThreshold(scored.Select(s => s.score).ToArray());

                    var entries = new List<PoolEntry>();

                    foreach (var (mix, score, bpm) in scored)
                    {
                        if (score < threshold)
                        {
                            continue;
                        }

                        IReadOnlySet<MoodLean> leanTags = ComputeLeanTags(mix, bpm, bpmTarget);
                        entries.Add(new PoolEntry(mix, leanTags));
                    }

                    pools[(slot, day)] = entries;
                }
            }

            return new NowSpinningPools { Pools = pools };
        }

        private static double ComputeThreshold(double[] scores)
        {
            Array.Sort(scores);
            int percentileIndex = (int)Math.Floor(scores.Length * (1.0 - PercentileKeepFraction));
            percentileIndex = Math.Clamp(percentileIndex, 0, scores.Length - 1);
            double percentileScore = scores[percentileIndex];
            return Math.Max(percentileScore, MinAbsoluteScore);
        }

        private static IReadOnlySet<MoodLean> ComputeLeanTags(Mix mix, int bpm, int bpmTarget)
        {
            var tags = new HashSet<MoodLean>();
            double warmth = mix.Warmth ?? 0.0;

            if (warmth < -0.3 && IsHighEnergy(mix.Energy))
            {
                tags.Add(MoodLean.Darker);
            }

            if (warmth > 0.3)
            {
                tags.Add(MoodLean.Warmer);
            }

            if (bpm < bpmTarget - 10)
            {
                tags.Add(MoodLean.Slower);
            }

            if (bpm > bpmTarget + 10)
            {
                tags.Add(MoodLean.Faster);
            }

            return tags;
        }

        private static bool IsHighEnergy(string? energy)
        {
            return string.Equals(energy, "peak", StringComparison.Ordinal)
                || string.Equals(energy, "high", StringComparison.Ordinal)
                || string.Equals(energy, "mid-peak", StringComparison.Ordinal)
                || string.Equals(energy, "mid-high", StringComparison.Ordinal);
        }
    }
}
