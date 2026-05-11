using System;
using System.Collections.Generic;
using System.Linq;
using Changsta.Ai.Core.Domain;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    internal static class WarmthScorer
    {
        private const double Normalizer = 1.8;

        public static IReadOnlyList<Mix> ComputeWarmth(
            IReadOnlyList<Mix> mixes,
            IReadOnlyDictionary<string, double> moodWeights,
            ILogger logger,
            out bool changed)
        {
            changed = false;
            var result = new Mix[mixes.Count];

            for (int i = 0; i < mixes.Count; i++)
            {
                Mix mix = mixes[i];
                double? warmth = Score(mix, moodWeights, logger);

                bool same = warmth == null
                    ? mix.Warmth == null
                    : mix.Warmth != null && warmth.Value == mix.Warmth.Value;

                if (!same)
                {
                    changed = true;
                    result[i] = WithWarmth(mix, warmth);
                }
                else
                {
                    result[i] = mix;
                }
            }

            return result;
        }

        private static double? Score(
            Mix mix,
            IReadOnlyDictionary<string, double> weights,
            ILogger logger)
        {
            if (mix.Moods.Count == 0)
            {
                return null;
            }

            var votes = new List<double>(mix.Moods.Count);

            foreach (string mood in mix.Moods)
            {
                string key = mood.Trim().ToLowerInvariant();

                if (weights.TryGetValue(key, out double w))
                {
                    if (w != 0.0)
                    {
                        votes.Add(w);
                    }
                }
                else
                {
                    logger.LogWarning("Unknown mood in warmth scoring: {Mood}", mood);
                }
            }

            if (votes.Count == 0)
            {
                return null;
            }

            double energyNudge = mix.Energy?.ToLowerInvariant() switch
            {
                "high" => -0.3,
                "low" => 0.3,
                _ => 0.0,
            };

            double avg = votes.Sum() / votes.Count;
            double raw = (avg + energyNudge) / Normalizer;
            return Math.Clamp(raw, -1.0, 1.0);
        }

        private static Mix WithWarmth(Mix mix, double? warmth)
        {
            return new Mix
            {
                Id = mix.Id,
                Title = mix.Title,
                Url = mix.Url,
                Description = mix.Description,
                Intro = mix.Intro,
                Duration = mix.Duration,
                ImageUrl = mix.ImageUrl,
                Tracklist = mix.Tracklist,
                Genre = mix.Genre,
                Energy = mix.Energy,
                BpmMin = mix.BpmMin,
                BpmMax = mix.BpmMax,
                Moods = mix.Moods,
                RelatedMixes = mix.RelatedMixes,
                PublishedAt = mix.PublishedAt,
                Warmth = warmth,
            };
        }
    }
}
