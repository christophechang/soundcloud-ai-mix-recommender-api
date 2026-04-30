using System;
using System.Collections.Generic;
using System.Linq;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    internal static class RelatedMixScorer
    {
        private const int MaxRelated = 6;
        private const int MaxSharedTracksCap = 5;
        private const int MaxSharedArtistsCap = 5;
        private const int MaxSharedMoodsCap = 3;
        private const int ScoreSharedTrack = 15;
        private const int ScoreSharedArtist = 8;
        private const int ScoreSameGenre = 6;
        private const int ScoreSameEnergy = 3;
        private const int ScoreSharedMood = 2;
        private const int ScoreBpmOverlap = 1;

        public static IReadOnlyList<Mix> ComputeRelatedMixes(IReadOnlyList<Mix> mixes, out bool changed)
        {
            changed = false;
            var result = new Mix[mixes.Count];

            for (int i = 0; i < mixes.Count; i++)
            {
                Mix mix = mixes[i];
                RelatedMixRef[] related = ScoreRelated(mix, mixes);

                if (!RelatedEquals(mix.RelatedMixes, related))
                {
                    changed = true;
                    result[i] = WithRelatedMixes(mix, related);
                }
                else
                {
                    result[i] = mix;
                }
            }

            return result;
        }

        private static RelatedMixRef[] ScoreRelated(Mix target, IReadOnlyList<Mix> all)
        {
            var trackSet = BuildTrackSet(target.Tracklist);
            var artistSet = BuildArtistSet(target.Tracklist);
            var moodSet = BuildMoodSet(target.Moods);

            return all
                .Where(m => !string.Equals(m.Url, target.Url, StringComparison.OrdinalIgnoreCase))
                .Select(m => (Mix: m, Score: Score(target, m, trackSet, artistSet, moodSet)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Mix.PublishedAt ?? DateTimeOffset.MinValue)
                .Take(MaxRelated)
                .Select(x => new RelatedMixRef
                {
                    Title = x.Mix.Title,
                    Url = x.Mix.Url,
                    ArtworkUrl = x.Mix.ImageUrl,
                })
                .ToArray();
        }

        private static int Score(
            Mix target,
            Mix candidate,
            HashSet<(string Artist, string Title)> targetTracks,
            HashSet<string> targetArtists,
            HashSet<string> targetMoods)
        {
            int score = 0;

            int sharedTracks = candidate.Tracklist
                .Count(t => targetTracks.Contains(
                    (t.Artist.ToLowerInvariant(), t.Title.ToLowerInvariant())));
            score += Math.Min(sharedTracks, MaxSharedTracksCap) * ScoreSharedTrack;

            int sharedArtists = candidate.Tracklist
                .Select(t => t.Artist.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .Count(a => targetArtists.Contains(a));
            score += Math.Min(sharedArtists, MaxSharedArtistsCap) * ScoreSharedArtist;

            if (!string.IsNullOrEmpty(target.Genre)
                && string.Equals(target.Genre, candidate.Genre, StringComparison.OrdinalIgnoreCase))
            {
                score += ScoreSameGenre;
            }

            if (!string.IsNullOrEmpty(target.Energy)
                && string.Equals(target.Energy, candidate.Energy, StringComparison.OrdinalIgnoreCase))
            {
                score += ScoreSameEnergy;
            }

            int sharedMoods = candidate.Moods
                .Count(m => targetMoods.Contains(m.ToLowerInvariant()));
            score += Math.Min(sharedMoods, MaxSharedMoodsCap) * ScoreSharedMood;

            if (BpmOverlap(target, candidate))
            {
                score += ScoreBpmOverlap;
            }

            return score;
        }

        private static bool BpmOverlap(Mix a, Mix b)
        {
            if (a.BpmMin is null || a.BpmMax is null || b.BpmMin is null || b.BpmMax is null)
            {
                return false;
            }

            return a.BpmMin <= b.BpmMax && b.BpmMin <= a.BpmMax;
        }

        private static HashSet<(string, string)> BuildTrackSet(IReadOnlyList<Track> tracks)
        {
            var set = new HashSet<(string, string)>();
            foreach (Track t in tracks)
            {
                set.Add((t.Artist.ToLowerInvariant(), t.Title.ToLowerInvariant()));
            }

            return set;
        }

        private static HashSet<string> BuildArtistSet(IReadOnlyList<Track> tracks)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (Track t in tracks)
            {
                set.Add(t.Artist.ToLowerInvariant());
            }

            return set;
        }

        private static HashSet<string> BuildMoodSet(IReadOnlyList<string> moods)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (string m in moods)
            {
                set.Add(m.ToLowerInvariant());
            }

            return set;
        }

        private static bool RelatedEquals(IReadOnlyList<RelatedMixRef> existing, RelatedMixRef[] computed)
        {
            if (existing.Count != computed.Length)
            {
                return false;
            }

            for (int i = 0; i < existing.Count; i++)
            {
                if (!string.Equals(existing[i].Url, computed[i].Url, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(existing[i].ArtworkUrl, computed[i].ArtworkUrl, StringComparison.Ordinal)
                    || !string.Equals(existing[i].Title, computed[i].Title, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static Mix WithRelatedMixes(Mix mix, IReadOnlyList<RelatedMixRef> related)
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
                RelatedMixes = related,
                PublishedAt = mix.PublishedAt,
            };
        }
    }
}
