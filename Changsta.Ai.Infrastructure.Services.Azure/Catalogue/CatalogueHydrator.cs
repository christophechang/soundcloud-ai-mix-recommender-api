using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Parsing;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    /// <summary>
    /// Fills in every field the catalogue derives rather than reads: intro text, related mixes,
    /// and warmth.
    /// </summary>
    internal sealed class CatalogueHydrator : ICatalogueHydrator
    {
        private readonly IMoodWeightResolver _moodWeights;
        private readonly ILogger _logger;

        public CatalogueHydrator(IMoodWeightResolver moodWeights, ILogger logger)
        {
            _moodWeights = moodWeights ?? throw new ArgumentNullException(nameof(moodWeights));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<CatalogueHydrationResult> HydrateAsync(
            IReadOnlyList<Mix> merged,
            CancellationToken cancellationToken)
        {
            merged = HydrateIntros(merged, out bool introHydrationChanged);
            merged = RelatedMixScorer.ComputeRelatedMixes(merged, out bool relatedMixesChanged);

            IReadOnlyDictionary<string, double> effectiveWeights =
                await _moodWeights.ResolveAsync(merged, cancellationToken).ConfigureAwait(false);

            merged = WarmthScorer.ComputeWarmth(merged, effectiveWeights, _logger, out bool warmthChanged);

            return new CatalogueHydrationResult
            {
                Mixes = merged,
                Changed = introHydrationChanged || relatedMixesChanged || warmthChanged,
            };
        }

        internal static IReadOnlyList<Mix> HydrateIntros(IReadOnlyList<Mix> mixes, out bool changed)
        {
            changed = false;
            var result = new Mix[mixes.Count];

            for (int i = 0; i < mixes.Count; i++)
            {
                Mix mix = mixes[i];

                if (mix.Intro is not null)
                {
                    result[i] = mix;
                    continue;
                }

                string? intro = MixDescriptionParser.ExtractIntro(mix.Description);

                if (intro is null)
                {
                    result[i] = mix;
                    continue;
                }

                changed = true;
                result[i] = mix with { Intro = intro };
            }

            return result;
        }
    }
}
