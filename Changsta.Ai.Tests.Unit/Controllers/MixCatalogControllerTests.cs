using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Interface.Api.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Changsta.Ai.Tests.Unit.Controllers
{
    [TestFixture]
    public sealed class MixCatalogControllerTests
    {
        // ── GET /api/catalog ─────────────────────────────────────────────────
        [Test]
        public async Task GetCatalogAsync_groups_artists_under_genre()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Shy One", "Track S")),
                MakeMix("2", "breaks", ("Anu", "Track A")),
                MakeMix("3", "dnb", ("Zinc", "Track Z")),
            };

            var page = await InvokeCatalogAsync(BuildSut(mixes), null, 1, 20);

            Assert.That(page.Items, Has.Length.EqualTo(2));
            Assert.That(page.Items[0].Genre, Is.EqualTo("breaks"));
            Assert.That(page.Items[0].Artists.Select(a => a.Name), Is.EqualTo(new[] { "Anu", "Shy One" }));
            Assert.That(page.Items[1].Genre, Is.EqualTo("dnb"));
        }

        [Test]
        public async Task GetCatalogAsync_filters_by_genre_case_insensitive()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Artist A", "Track 1")),
                MakeMix("2", "dnb", ("Artist B", "Track 2")),
                MakeMix("3", "breaks", ("Artist C", "Track 3")),
            };

            var page = await InvokeCatalogAsync(BuildSut(mixes), "Breaks", 1, 20);

            Assert.That(page.Total, Is.EqualTo(1));
            Assert.That(page.Items[0].Genre, Is.EqualTo("breaks"));
            Assert.That(page.Items[0].Artists, Has.Length.EqualTo(2));
        }

        [Test]
        public async Task GetCatalogAsync_normalises_genre_alias()
        {
            var mixes = new[]
            {
                MakeMix("1", "deep-house", ("Artist A", "Track 1")),
                MakeMix("2", "dnb", ("Artist B", "Track 2")),
            };

            var page = await InvokeCatalogAsync(BuildSut(mixes), "deephouse", 1, 20);

            Assert.That(page.Total, Is.EqualTo(1));
            Assert.That(page.Items[0].Genre, Is.EqualTo("deep-house"));
        }

        [Test]
        public async Task GetCatalogAsync_merges_tracks_for_same_artist_across_mixes()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Shy One", "Alpha")),
                MakeMix("2", "breaks", ("Shy One", "Beta")),
            };

            var page = await InvokeCatalogAsync(BuildSut(mixes), "breaks", 1, 20);

            Assert.That(page.Items[0].Artists, Has.Length.EqualTo(1));
            Assert.That(page.Items[0].Artists[0].Tracks, Is.EqualTo(new[] { "Alpha", "Beta" }));
        }

        [Test]
        public async Task GetCatalogAsync_deduplicates_tracks_for_same_artist()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Shy One", "Alpha")),
                MakeMix("2", "breaks", ("Shy One", "Alpha")),
            };

            var page = await InvokeCatalogAsync(BuildSut(mixes), "breaks", 1, 20);

            Assert.That(page.Items[0].Artists[0].Tracks, Has.Length.EqualTo(1));
        }

        [Test]
        public async Task GetCatalogAsync_pages_over_genre_groups()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Artist A", "Track 1")),
                MakeMix("2", "dnb", ("Artist B", "Track 2")),
                MakeMix("3", "house", ("Artist C", "Track 3")),
                MakeMix("4", "techno", ("Artist D", "Track 4")),
                MakeMix("5", "uk-bass", ("Artist E", "Track 5")),
            };

            var page = await InvokeCatalogAsync(BuildSut(mixes), null, 2, 2);

            Assert.That(page.Items.Select(g => g.Genre), Is.EqualTo(new[] { "house", "techno" }));
        }

        [Test]
        public async Task GetCatalogAsync_returns_empty_items_beyond_last_page()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Artist A", "Track 1")),
                MakeMix("2", "dnb", ("Artist B", "Track 2")),
            };

            var page = await InvokeCatalogAsync(BuildSut(mixes), null, 99, 20);

            Assert.That(page.Total, Is.EqualTo(2));
            Assert.That(page.Items.Any(), Is.False);
        }

        [Test]
        public async Task GetCatalogAsync_returns_400_for_page_less_than_1()
        {
            IActionResult result = await BuildSut(Array.Empty<Mix>())
                .GetCatalogAsync(null, 0, 20, CancellationToken.None);

            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        }

        [Test]
        public async Task GetCatalogAsync_returns_400_for_page_size_over_100()
        {
            IActionResult result = await BuildSut(Array.Empty<Mix>())
                .GetCatalogAsync(null, 1, 101, CancellationToken.None);

            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        }

        [Test]
        public async Task GetCatalogAsync_total_counts_genres_not_artists()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Artist A", "Track 1"), ("Artist B", "Track 2")),
                MakeMix("2", "dnb", ("Artist C", "Track 3")),
            };

            var page = await InvokeCatalogAsync(BuildSut(mixes), null, 1, 20);

            Assert.That(page.Total, Is.EqualTo(2));
            Assert.That(page.Items[0].Artists, Has.Length.EqualTo(2));
        }

        [Test]
        public async Task GetCatalogAsync_returns_correct_total_pages()
        {
            Mix[] mixes = Enumerable.Range(1, 47)
                .Select(i => MakeMix(i.ToString(), $"genre-{i:D3}", ($"Artist {i}", "Track 1")))
                .ToArray();

            var page = await InvokeCatalogAsync(BuildSut(mixes), null, 1, 20);

            Assert.That(page.TotalPages, Is.EqualTo(3));
        }

        [Test]
        public async Task GetCatalogAsync_mix_with_no_tracklist_contributes_no_genre_group()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks"),
                MakeMix("2", "dnb", ("Artist A", "Track 1")),
            };

            var page = await InvokeCatalogAsync(BuildSut(mixes), null, 1, 20);

            Assert.That(page.Total, Is.EqualTo(1));
            Assert.That(page.Items[0].Genre, Is.EqualTo("dnb"));
        }

        // ── GET /api/catalog/artists ──────────────────────────────────────────
        [Test]
        public async Task GetArtistsAsync_orders_by_track_count_desc()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Shy One", "Track 1"), ("Shy One", "Track 2"), ("Shy One", "Track 3")),
                MakeMix("2", "dnb", ("Calibre", "Track A")),
            };

            var page = await InvokeArtistsAsync(BuildSut(mixes), 1, 20);

            Assert.That(page.Items[0].Name, Is.EqualTo("Shy One"));
            Assert.That(page.Items[0].TrackCount, Is.EqualTo(3));
            Assert.That(page.Items[1].Name, Is.EqualTo("Calibre"));
        }

        [Test]
        public async Task GetArtistsAsync_uses_name_asc_as_tiebreaker()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Zinc", "Track 1")),
                MakeMix("2", "dnb", ("Anu", "Track 2")),
            };

            var page = await InvokeArtistsAsync(BuildSut(mixes), 1, 20);

            Assert.That(page.Items.Select(a => a.Name), Is.EqualTo(new[] { "Anu", "Zinc" }));
        }

        [Test]
        public async Task GetArtistsAsync_merges_tracks_across_genres()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Shy One", "Alpha")),
                MakeMix("2", "dnb", ("Shy One", "Beta")),
            };

            var page = await InvokeArtistsAsync(BuildSut(mixes), 1, 20);

            Assert.That(page.Total, Is.EqualTo(1));
            Assert.That(page.Items[0].TrackCount, Is.EqualTo(2));
            Assert.That(page.Items[0].Tracks, Is.EqualTo(new[] { "Alpha", "Beta" }));
        }

        [Test]
        public async Task GetArtistsAsync_deduplicates_tracks()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Shy One", "Alpha")),
                MakeMix("2", "dnb", ("Shy One", "Alpha")),
            };

            var page = await InvokeArtistsAsync(BuildSut(mixes), 1, 20);

            Assert.That(page.Items[0].TrackCount, Is.EqualTo(1));
        }

        [Test]
        public async Task GetArtistsAsync_returns_correct_page()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Artist A", "Track 1"), ("Artist A", "Track 2"), ("Artist A", "Track 3")),
                MakeMix("2", "breaks", ("Artist B", "Track 4"), ("Artist B", "Track 5")),
                MakeMix("3", "breaks", ("Artist C", "Track 6")),
                MakeMix("4", "breaks", ("Artist D", "Track 7")),
            };

            var page = await InvokeArtistsAsync(BuildSut(mixes), 2, 2);

            Assert.That(page.Items.Select(a => a.Name), Is.EqualTo(new[] { "Artist C", "Artist D" }));
        }

        [Test]
        public async Task GetArtistsAsync_returns_correct_total()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Artist A", "Track 1")),
                MakeMix("2", "breaks", ("Artist B", "Track 2")),
                MakeMix("3", "dnb", ("Artist C", "Track 3")),
            };

            var page = await InvokeArtistsAsync(BuildSut(mixes), 1, 20);

            Assert.That(page.Total, Is.EqualTo(3));
        }

        [Test]
        public async Task GetArtistsAsync_returns_400_for_invalid_page()
        {
            IActionResult result = await BuildSut(Array.Empty<Mix>())
                .GetArtistsAsync(0, 20, CancellationToken.None);

            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        }

        [Test]
        public async Task GetArtistsAsync_returns_400_for_invalid_page_size()
        {
            IActionResult result = await BuildSut(Array.Empty<Mix>())
                .GetArtistsAsync(1, 101, CancellationToken.None);

            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        }

        // ── GET /api/catalog/artists/{name}/mixes ────────────────────────────
        [Test]
        public async Task GetMixesByArtistAsync_returns_mixes_containing_artist()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Shy One", "Track 1")),
                MakeMix("2", "dnb", ("Calibre", "Track 2")),
                MakeMix("3", "breaks", ("Shy One", "Track 3"), ("Calibre", "Track 4")),
            };

            Mix[] results = await InvokeMixesByArtistAsync(BuildSut(mixes), "Shy One");

            Assert.That(results.Select(m => m.Id), Is.EquivalentTo(new[] { "1", "3" }));
        }

        [Test]
        public async Task GetMixesByArtistAsync_is_case_insensitive()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Shy One", "Track 1")),
                MakeMix("2", "dnb", ("Calibre", "Track 2")),
            };

            Mix[] results = await InvokeMixesByArtistAsync(BuildSut(mixes), "shy one");

            Assert.That(results, Has.Length.EqualTo(1));
            Assert.That(results[0].Id, Is.EqualTo("1"));
        }

        [Test]
        public async Task GetMixesByArtistAsync_returns_empty_when_artist_not_found()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Shy One", "Track 1")),
            };

            Mix[] results = await InvokeMixesByArtistAsync(BuildSut(mixes), "Unknown Artist");

            Assert.That(results, Is.Empty);
        }

        [Test]
        public async Task GetMixesByArtistAsync_returns_mix_once_even_with_multiple_tracks_by_artist()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Shy One", "Track 1"), ("Shy One", "Track 2")),
            };

            Mix[] results = await InvokeMixesByArtistAsync(BuildSut(mixes), "Shy One");

            Assert.That(results, Has.Length.EqualTo(1));
        }

        // ── NormalizeGenre null safety ────────────────────────────────────────
        [Test]
        public async Task GetCatalogAsync_mix_with_null_genre_does_not_throw()
        {
            var mixes = new[]
            {
                MakeMix("1", "dnb", ("Artist A", "Track 1")),
                new Mix
                {
                    Id = "2",
                    Title = "Null Genre Mix",
                    Url = "https://sc.test/mix-2",
                    Genre = null!,
                    Energy = "high",
                    Tracklist = new[] { new Track { Artist = "Artist B", Title = "Track 2" } },
                },
            };

            var page = await InvokeCatalogAsync(BuildSut(mixes), null, 1, 20);

            Assert.That(page.Total, Is.GreaterThanOrEqualTo(1));
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static async Task<MixCatalogController.CatalogPage<MixCatalogController.GenreEntry>> InvokeCatalogAsync(
            MixCatalogController sut,
            string? genre,
            int page,
            int pageSize)
        {
            IActionResult result = await sut.GetCatalogAsync(genre, page, pageSize, CancellationToken.None);
            return (MixCatalogController.CatalogPage<MixCatalogController.GenreEntry>)((OkObjectResult)result).Value!;
        }

        private static async Task<MixCatalogController.CatalogPage<MixCatalogController.ArtistSummary>> InvokeArtistsAsync(
            MixCatalogController sut,
            int page,
            int pageSize)
        {
            IActionResult result = await sut.GetArtistsAsync(page, pageSize, CancellationToken.None);
            return (MixCatalogController.CatalogPage<MixCatalogController.ArtistSummary>)((OkObjectResult)result).Value!;
        }

        private static async Task<Mix[]> InvokeMixesByArtistAsync(MixCatalogController sut, string name)
        {
            IActionResult result = await sut.GetMixesByArtistAsync(name, CancellationToken.None);
            return (Mix[])((OkObjectResult)result).Value!;
        }

        private static MixCatalogController BuildSut(IReadOnlyList<Mix> mixes)
        {
            return new MixCatalogController(new StubMixCatalogueProvider(mixes));
        }

        private static Mix MakeMix(string id, string genre, params (string Artist, string Title)[] tracks)
        {
            return new Mix
            {
                Id = id,
                Title = $"Mix {id}",
                Url = $"https://sc.test/mix-{id}",
                Genre = genre,
                Energy = "high",
                Tracklist = tracks
                    .Select(t => new Track { Artist = t.Artist, Title = t.Title })
                    .ToArray(),
            };
        }

        private sealed class StubMixCatalogueProvider : IMixCatalogueProvider
        {
            private readonly IReadOnlyList<Mix> _mixes;

            public StubMixCatalogueProvider(IReadOnlyList<Mix> mixes)
            {
                _mixes = mixes;
            }

            public Task<IReadOnlyList<Mix>> GetLatestAsync(int maxItems, CancellationToken cancellationToken)
            {
                return Task.FromResult(_mixes);
            }
        }
    }
}
