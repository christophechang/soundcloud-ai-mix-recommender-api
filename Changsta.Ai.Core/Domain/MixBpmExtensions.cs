using System;

namespace Changsta.Ai.Core.Domain
{
    public static class MixBpmExtensions
    {
        /// <summary>
        /// The representative mid-point BPM for a mix: the rounded average of BpmMin/BpmMax when
        /// both are present, otherwise whichever single bound is set (or null). Single source for
        /// the mid-BPM previously duplicated in the radio mappers/scorer and the catalogue compass.
        /// See issue #49.
        /// </summary>
        public static int? GetMidBpm(this Mix mix)
        {
            ArgumentNullException.ThrowIfNull(mix);

            if (mix.BpmMin.HasValue && mix.BpmMax.HasValue)
            {
                return (int)Math.Round((mix.BpmMin.Value + mix.BpmMax.Value) / 2.0);
            }

            return mix.BpmMin ?? mix.BpmMax;
        }
    }
}
