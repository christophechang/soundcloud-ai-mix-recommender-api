using System;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Interface.Api.ViewModels
{
    internal static class RadioMixMapper
    {
        internal static RadioMixVm MapMix(Mix mix)
        {
            return new RadioMixVm
            {
                Id = mix.Id,
                Title = mix.Title,
                Url = mix.Url,
                Intro = mix.Intro,
                ImageUrl = mix.ImageUrl,
                Genre = mix.Genre,
                Energy = mix.Energy,
                Bpm = mix.GetMidBpm(),
                Moods = mix.Moods,
                PublishedAt = mix.PublishedAt,
                Duration = ParseDurationSeconds(mix.Duration),
                Tracklist = mix.Tracklist,
            };
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
