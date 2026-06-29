using System;
using System.Collections.Generic;
using System.Linq;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    /// <summary>
    /// Merges the persisted blob catalogue with the live SoundCloud RSS feed: URL/legacy permalink
    /// migration, schema-field sync on description change, new-discovery ordering, and the
    /// new/updated counts that drive the write-back decision. Extracted verbatim from
    /// BlobBackedMixCatalogueProvider as a pure, independently-testable collaborator. See issue #30.
    /// </summary>
    internal static class MixCatalogueMerger
    {
        private const int MinTracksForNearEquivalentLegacyMatch = 8;

        public static IReadOnlyList<Mix> Merge(
            IReadOnlyList<Mix> blobMixes,
            IReadOnlyList<Mix> rssMixes)
        {
            var byUrl = new Dictionary<string, Mix>(StringComparer.OrdinalIgnoreCase);
            var byId = new Dictionary<string, Mix>(StringComparer.OrdinalIgnoreCase);

            foreach (var mix in blobMixes)
            {
                byUrl[mix.Url] = mix;
                if (!string.IsNullOrEmpty(mix.Id))
                {
                    byId[mix.Id] = mix;
                }
            }

            // Maps old blob URL → new RSS URL when a mix's SoundCloud permalink changes.
            var movedOldToNew = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var mix in rssMixes)
            {
                if (byUrl.TryGetValue(mix.Url, out Mix? existing))
                {
                    // When description changes and the RSS mix has a valid changsta schema block
                    // (indicated by a non-empty Genre), sync all schema fields from RSS so that
                    // edits to the SoundCloud description are reflected on the next cache flush.
                    // Without a schema block the blob metadata is preserved unchanged.
                    bool descriptionChanged = !string.Equals(
                        mix.Description, existing.Description, StringComparison.Ordinal);
                    bool rssHasSchema = !string.IsNullOrEmpty(mix.Genre);
                    bool syncSchema = descriptionChanged && rssHasSchema;

                    // Backfill a stale empty tracklist: a mix first ingested under an older parser
                    // can have an empty persisted tracklist that the description-change gate never
                    // re-syncs (the description text is unchanged). When RSS now parses a non-empty
                    // tracklist, adopt it so improved parsing heals existing entries. See issue #119.
                    bool backfillTracklist =
                        rssHasSchema && existing.Tracklist.Count == 0 && mix.Tracklist.Count > 0;

                    // Base = existing (blob) so computed fields (Url, RelatedMixes, Warmth)
                    // carry forward by default; only RSS-sourced/synced fields are overridden.
                    byUrl[mix.Url] = existing with
                    {
                        Id = ResolveStableId(existing.Id, mix.Id),
                        Title = mix.Title,
                        Description = mix.Description,
                        Intro = mix.Intro,
                        Duration = mix.Duration ?? existing.Duration,
                        ImageUrl = mix.ImageUrl ?? existing.ImageUrl,
                        Tracklist = (syncSchema || backfillTracklist) ? mix.Tracklist : existing.Tracklist,
                        Genre = syncSchema ? mix.Genre : existing.Genre,
                        Energy = syncSchema ? mix.Energy : existing.Energy,
                        BpmMin = syncSchema ? mix.BpmMin : existing.BpmMin,
                        BpmMax = syncSchema ? mix.BpmMax : existing.BpmMax,
                        Moods = syncSchema ? mix.Moods : existing.Moods,
                        PublishedAt = mix.PublishedAt ?? existing.PublishedAt,
                    };

                    if (TryFindLegacyMovedEntry(mix, blobMixes, out Mix? legacyEntry))
                    {
                        movedOldToNew[legacyEntry.Url] = mix.Url;
                    }
                }
                else if (!string.IsNullOrEmpty(mix.Id)
                    && byId.TryGetValue(mix.Id, out Mix? priorEntry))
                {
                    // URL changed: same SoundCloud track ID, new permalink — transfer computed
                    // data to the new URL and retire the old one.
                    movedOldToNew[priorEntry.Url] = mix.Url;

                    bool descriptionChanged = !string.Equals(
                        mix.Description, priorEntry.Description, StringComparison.Ordinal);
                    bool rssHasSchema = !string.IsNullOrEmpty(mix.Genre);
                    bool syncSchema = descriptionChanged && rssHasSchema;

                    // Base = priorEntry (blob) so RelatedMixes/Warmth carry forward; the URL
                    // moved, so Url is overridden to the new RSS permalink.
                    byUrl[mix.Url] = priorEntry with
                    {
                        Id = ResolveStableId(priorEntry.Id, mix.Id),
                        Title = mix.Title,
                        Url = mix.Url,
                        Description = mix.Description,
                        Intro = mix.Intro,
                        Duration = mix.Duration ?? priorEntry.Duration,
                        ImageUrl = mix.ImageUrl ?? priorEntry.ImageUrl,
                        Tracklist = syncSchema ? mix.Tracklist : priorEntry.Tracklist,
                        Genre = syncSchema ? mix.Genre : priorEntry.Genre,
                        Energy = syncSchema ? mix.Energy : priorEntry.Energy,
                        BpmMin = syncSchema ? mix.BpmMin : priorEntry.BpmMin,
                        BpmMax = syncSchema ? mix.BpmMax : priorEntry.BpmMax,
                        Moods = syncSchema ? mix.Moods : priorEntry.Moods,
                        PublishedAt = mix.PublishedAt ?? priorEntry.PublishedAt,
                    };
                }
                else if (TryFindLegacyMovedEntry(mix, blobMixes, out Mix? legacyEntry))
                {
                    // Earlier catalog rows used the SoundCloud URL as Id. If a permalink
                    // changes before that row has been hydrated with the stable RSS GUID,
                    // use immutable metadata and tracklist evidence to migrate it once.
                    movedOldToNew[legacyEntry.Url] = mix.Url;

                    bool descriptionChanged = !string.Equals(
                        mix.Description, legacyEntry.Description, StringComparison.Ordinal);
                    bool rssHasSchema = !string.IsNullOrEmpty(mix.Genre);
                    bool syncSchema = descriptionChanged && rssHasSchema;
                    bool syncTracklist = syncSchema && SameTracklist(legacyEntry.Tracklist, mix.Tracklist);

                    // Base = legacyEntry (blob) so RelatedMixes/Warmth carry forward; the URL
                    // moved, so Url is overridden to the new RSS permalink.
                    byUrl[mix.Url] = legacyEntry with
                    {
                        Id = ResolveStableId(legacyEntry.Id, mix.Id),
                        Title = mix.Title,
                        Url = mix.Url,
                        Description = mix.Description,
                        Intro = mix.Intro,
                        Duration = mix.Duration ?? legacyEntry.Duration,
                        ImageUrl = mix.ImageUrl ?? legacyEntry.ImageUrl,
                        Tracklist = syncTracklist ? mix.Tracklist : legacyEntry.Tracklist,
                        Genre = syncSchema ? mix.Genre : legacyEntry.Genre,
                        Energy = syncSchema ? mix.Energy : legacyEntry.Energy,
                        BpmMin = syncSchema ? mix.BpmMin : legacyEntry.BpmMin,
                        BpmMax = syncSchema ? mix.BpmMax : legacyEntry.BpmMax,
                        Moods = syncSchema ? mix.Moods : legacyEntry.Moods,
                        PublishedAt = mix.PublishedAt ?? legacyEntry.PublishedAt,
                    };
                }
                else
                {
                    byUrl[mix.Url] = mix;
                }
            }

            var blobUrlSet = new HashSet<string>(
                blobMixes.Select(m => m.Url),
                StringComparer.OrdinalIgnoreCase);

            var movedNewUrls = new HashSet<string>(movedOldToNew.Values, StringComparer.OrdinalIgnoreCase);

            var result = new List<Mix>(byUrl.Count);

            // New RSS discoveries (not in blob, not a URL-moved entry) go first — newest at front
            foreach (var mix in rssMixes)
            {
                if (!blobUrlSet.Contains(mix.Url) && !movedNewUrls.Contains(mix.Url) && !IsMetadataOnlyRssMix(mix))
                {
                    result.Add(mix);
                }
            }

            // Blob entries follow in their original order; moved entries use the new URL, orphaned old URLs are dropped
            foreach (var mix in blobMixes)
            {
                if (movedOldToNew.TryGetValue(mix.Url, out string? newUrl))
                {
                    if (!blobUrlSet.Contains(newUrl))
                    {
                        result.Add(byUrl[newUrl]);
                    }
                }
                else
                {
                    result.Add(byUrl[mix.Url]);
                }
            }

            return result;
        }

        public static int CountNewDiscoveries(
            IReadOnlyList<Mix> blobMixes,
            IReadOnlyList<Mix> rssMixes)
        {
            if (rssMixes.Count == 0)
            {
                return 0;
            }

            var blobUrls = new HashSet<string>(
                blobMixes.Select(m => m.Url),
                StringComparer.OrdinalIgnoreCase);

            return rssMixes.Count(m => !blobUrls.Contains(m.Url) && !IsMetadataOnlyRssMix(m));
        }

        public static int CountUpdatedEntries(
            IReadOnlyList<Mix> blobMixes,
            IReadOnlyList<Mix> rssMixes)
        {
            if (rssMixes.Count == 0)
            {
                return 0;
            }

            var blobByUrl = new Dictionary<string, Mix>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < blobMixes.Count; i++)
            {
                blobByUrl[blobMixes[i].Url] = blobMixes[i];
            }

            int count = 0;

            for (int i = 0; i < rssMixes.Count; i++)
            {
                Mix rssMix = rssMixes[i];

                if (!blobByUrl.TryGetValue(rssMix.Url, out Mix? blobMix))
                {
                    continue;
                }

                if (!string.Equals(rssMix.Description, blobMix.Description, StringComparison.Ordinal)
                    || !string.Equals(rssMix.Title, blobMix.Title, StringComparison.Ordinal))
                {
                    count++;
                    continue;
                }

                string? effectiveDuration = rssMix.Duration ?? blobMix.Duration;
                string? effectiveImageUrl = rssMix.ImageUrl ?? blobMix.ImageUrl;
                string effectiveId = ResolveStableId(blobMix.Id, rssMix.Id);

                if (!string.Equals(effectiveDuration, blobMix.Duration, StringComparison.Ordinal)
                    || !string.Equals(effectiveImageUrl, blobMix.ImageUrl, StringComparison.Ordinal)
                    || !string.Equals(effectiveId, blobMix.Id, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsMetadataOnlyRssMix(Mix mix)
        {
            return string.IsNullOrWhiteSpace(mix.Genre)
                && string.IsNullOrWhiteSpace(mix.Energy)
                && mix.Tracklist.Count == 0
                && mix.Moods.Count == 0
                && mix.BpmMin is null
                && mix.BpmMax is null;
        }

        private static bool TryFindLegacyMovedEntry(
            Mix rssMix,
            IReadOnlyList<Mix> blobMixes,
            out Mix legacyEntry)
        {
            for (int i = 0; i < blobMixes.Count; i++)
            {
                Mix candidate = blobMixes[i];

                if (!IsUrlLikeId(candidate.Id)
                    || string.Equals(candidate.Url, rssMix.Url, StringComparison.OrdinalIgnoreCase)
                    || !SamePublishedAt(candidate, rssMix)
                    || !EquivalentTracklist(candidate.Tracklist, rssMix.Tracklist))
                {
                    continue;
                }

                legacyEntry = candidate;
                return true;
            }

            legacyEntry = null!;
            return false;
        }

        private static bool SamePublishedAt(Mix a, Mix b)
        {
            if (a.PublishedAt is null || b.PublishedAt is null)
            {
                return false;
            }

            return a.PublishedAt.Value.Equals(b.PublishedAt.Value);
        }

        private static bool SameTracklist(IReadOnlyList<Track> a, IReadOnlyList<Track> b)
        {
            if (a.Count == 0 || a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (!SameTrack(a[i], b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool EquivalentTracklist(IReadOnlyList<Track> a, IReadOnlyList<Track> b)
        {
            if (a.Count == 0 || a.Count != b.Count)
            {
                return false;
            }

            int matched = 0;

            for (int i = 0; i < a.Count; i++)
            {
                if (SameTrack(a[i], b[i]) || SameTrackWithArtistTitleSwapped(a[i], b[i]))
                {
                    matched++;
                }
            }

            return matched == a.Count
                || (a.Count >= MinTracksForNearEquivalentLegacyMatch && matched >= a.Count - 1);
        }

        private static bool SameTrack(Track a, Track b)
        {
            return string.Equals(a.Artist, b.Artist, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameTrackWithArtistTitleSwapped(Track a, Track b)
        {
            return string.Equals(a.Artist, b.Title, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Title, b.Artist, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveStableId(string existingId, string rssId)
        {
            if (IsUrlLikeId(existingId) && !string.IsNullOrWhiteSpace(rssId) && !IsUrlLikeId(rssId))
            {
                return rssId;
            }

            return existingId;
        }

        private static bool IsUrlLikeId(string id)
        {
            return id.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }
    }
}
