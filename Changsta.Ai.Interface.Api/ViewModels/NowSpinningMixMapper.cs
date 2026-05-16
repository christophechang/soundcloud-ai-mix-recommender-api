using System;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Interface.Api.ViewModels
{
    internal static class NowSpinningMixMapper
    {
        internal static NowSpinningMixVm MapMix(Mix mix)
        {
            return new NowSpinningMixVm
            {
                Id = mix.Id,
                Title = mix.Title,
                Url = mix.Url,
                Genre = mix.Genre,
                Energy = mix.Energy,
                Bpm = ComputeBpm(mix),
                Moods = mix.Moods,
                PublishedAt = mix.PublishedAt,
                Duration = ParseDurationSeconds(mix.Duration),
            };
        }

        private static int? ComputeBpm(Mix mix)
        {
            if (mix.BpmMin.HasValue && mix.BpmMax.HasValue)
            {
                return (int)Math.Round((mix.BpmMin.Value + mix.BpmMax.Value) / 2.0);
            }

            return mix.BpmMin ?? mix.BpmMax;
        }

        private static int? ParseDurationSeconds(string? duration)
        {
            if (duration is null)
            {
                return null;
            }

            if (TimeSpan.TryParse(duration, out TimeSpan ts))
            {
                return (int)ts.TotalSeconds;
            }

            return null;
        }
    }
}
