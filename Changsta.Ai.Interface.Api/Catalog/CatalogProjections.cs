using System;
using System.Collections.Generic;
using System.Linq;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Normalization;
using Changsta.Ai.Interface.Api.ViewModels;

namespace Changsta.Ai.Interface.Api.Catalog
{
    /// <summary>
    /// Pure projections over the mix catalogue used by <c>MixCatalogController</c>. Extracted from
    /// the controller so the grouping/dedup/compass logic is unit-testable without going through
    /// <c>ControllerBase</c>, and the controller stays thin. See issue #49.
    /// </summary>
    public static class CatalogProjections
    {
        public static string[] GenreNames(IReadOnlyList<Mix> mixes) =>
            mixes
                .Where(m => !string.IsNullOrWhiteSpace(m.Genre))
                .Select(m => GenreNormalizer.Normalize(m.Genre))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        public static string[] ArtistNames(IReadOnlyList<Mix> mixes) =>
            mixes
                .SelectMany(m => m.Tracklist)
                .Select(t => t.Artist)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        public static Mix[] MixesOrdered(IReadOnlyList<Mix> mixes, string? genre) =>
            FilterByGenre(mixes, genre)
                .OrderByDescending(m => m.PublishedAt ?? DateTimeOffset.MinValue)
                .ToArray();

        public static GenreEntry[] GenreTree(IReadOnlyList<Mix> mixes, string? genre)
        {
            var byGenre = new Dictionary<string, Dictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);

            foreach (Mix mix in FilterByGenre(mixes, genre))
            {
                string normalisedGenre = GenreNormalizer.Normalize(mix.Genre);

                if (!byGenre.TryGetValue(normalisedGenre, out Dictionary<string, SortedSet<string>>? byArtist))
                {
                    byArtist = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
                    byGenre[normalisedGenre] = byArtist;
                }

                foreach (Track track in mix.Tracklist)
                {
                    if (!byArtist.TryGetValue(track.Artist, out SortedSet<string>? titles))
                    {
                        titles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                        byArtist[track.Artist] = titles;
                    }

                    titles.Add(track.Title);
                }
            }

            return byGenre
                .Where(g => g.Value.Count > 0)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new GenreEntry
                {
                    Genre = g.Key,
                    Artists = g.Value
                        .OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(a => new ArtistEntry
                        {
                            Name = a.Key,
                            Tracks = a.Value.ToArray(),
                        })
                        .ToArray(),
                })
                .ToArray();
        }

        public static TrackSummary[] TrackSummaries(IReadOnlyList<Mix> mixes) =>
            mixes
                .SelectMany(m => m.Tracklist.Select(t => (Mix: m, Track: t)))
                .GroupBy(a => (
                    Artist: a.Track.Artist.Trim().ToLowerInvariant(),
                    Title: a.Track.Title.Trim().ToLowerInvariant()))
                .Select(g =>
                {
                    Track first = g.First().Track;
                    return new TrackSummary
                    {
                        Artist = first.Artist.Trim(),
                        Title = first.Title.Trim(),
                        RecurrenceCount = g.Count(),
                        GenresSeen = g
                            .Select(a => GenreNormalizer.Normalize(a.Mix.Genre))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                            .ToArray(),
                    };
                })
                .OrderByDescending(t => t.RecurrenceCount)
                .ThenBy(t => t.Artist, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        public static MixTitleEntry[] MixTitles(IReadOnlyList<Mix> mixes) =>
            mixes
                .OrderByDescending(m => m.PublishedAt ?? DateTimeOffset.MinValue)
                .Select(m => new MixTitleEntry
                {
                    Title = m.Title,
                    Slug = MixSlugHelper.ExtractSlug(m.Url),
                })
                .ToArray();

        public static CompassEntry[] Compass(IReadOnlyList<Mix> mixes) =>
            mixes
                .Where(m => m.Warmth.HasValue)
                .Select(m => new CompassEntry
                {
                    Slug = MixSlugHelper.ExtractSlug(m.Url),
                    Title = m.Title,
                    Url = m.Url,
                    ImageUrl = m.ImageUrl,
                    Genre = m.Genre,
                    Energy = m.Energy,

                    // Preserves the compass's existing truncating mid-BPM (int division) to avoid a
                    // response-contract value change. The radio surface uses Mix.GetMidBpm() (rounded)
                    // — see issue #49 note.
                    Bpm = m.BpmMin.HasValue && m.BpmMax.HasValue
                        ? (m.BpmMin.Value + m.BpmMax.Value) / 2
                        : m.BpmMin ?? m.BpmMax,
                    BpmMin = m.BpmMin,
                    BpmMax = m.BpmMax,
                    Warmth = m.Warmth!.Value,
                    Moods = m.Moods,
                    PublishedAt = m.PublishedAt,
                })
                .ToArray();

        private static IEnumerable<Mix> FilterByGenre(IReadOnlyList<Mix> mixes, string? genre)
        {
            if (string.IsNullOrWhiteSpace(genre))
            {
                return mixes;
            }

            string normalisedQuery = GenreNormalizer.Normalize(genre);
            return mixes.Where(m =>
                string.Equals(GenreNormalizer.Normalize(m.Genre), normalisedQuery, StringComparison.OrdinalIgnoreCase));
        }
    }
}
