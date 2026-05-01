using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Interface.Api.Controllers;
using Changsta.Ai.Interface.Api.ViewModels;
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
            Assert.That(page.Items[0].Genre, Is.EqualTo("breakbeat"));
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
            Assert.That(page.Items[0].Genre, Is.EqualTo("breakbeat"));
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
        public async Task GetCatalogAsync_returns_400_for_page_size_over_200()
        {
            IActionResult result = await BuildSut(Array.Empty<Mix>())
                .GetCatalogAsync(null, 1, 201, CancellationToken.None);

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

        // ── GET /api/catalog/genres ──────────────────────────────────────────
        [Test]
        public async Task GetGenresAsync_returns_sorted_distinct_normalised_genres()
        {
            var mixes = new[]
            {
                MakeMix("1", "house", ("Artist A", "Track 1")),
                MakeMix("2", "dnb", ("Artist B", "Track 2")),
                MakeMix("3", "breaks", ("Artist C", "Track 3")),
                MakeMix("4", "dnb", ("Artist D", "Track 4")),
            };

            string[] genres = await InvokeGenresAsync(BuildSut(mixes));

            Assert.That(genres, Is.EqualTo(new[] { "breakbeat", "dnb", "house" }));
        }

        [Test]
        public async Task GetGenresAsync_normalises_aliases()
        {
            var mixes = new[]
            {
                MakeMix("1", "deep-house", ("Artist A", "Track 1")),
                MakeMix("2", "deephouse", ("Artist B", "Track 2")),
            };

            string[] genres = await InvokeGenresAsync(BuildSut(mixes));

            Assert.That(genres, Is.EqualTo(new[] { "deep-house" }));
        }

        [Test]
        public async Task GetGenresAsync_excludes_mixes_with_null_or_whitespace_genre()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Artist A", "Track 1")),
                new Mix
                {
                    Id = "2",
                    Title = "No Genre",
                    Url = "https://sc.test/mix-2",
                    Genre = null!,
                    Energy = "high",
                    Tracklist = Array.Empty<Track>(),
                },
            };

            string[] genres = await InvokeGenresAsync(BuildSut(mixes));

            Assert.That(genres, Is.EqualTo(new[] { "breakbeat" }));
        }

        [Test]
        public async Task GetGenresAsync_returns_empty_when_no_mixes()
        {
            string[] genres = await InvokeGenresAsync(BuildSut(Array.Empty<Mix>()));

            Assert.That(genres, Is.Empty);
        }

        // ── GET /api/catalog/mixes ───────────────────────────────────────────
        [Test]
        public async Task GetMixesAsync_no_genre_returns_all_mixes()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Artist A", "Track 1")),
                MakeMix("2", "dnb", ("Artist B", "Track 2")),
                MakeMix("3", "house", ("Artist C", "Track 3")),
            };

            var page = await InvokeMixesAsync(BuildSut(mixes), null, 1, 20);

            Assert.That(page.Total, Is.EqualTo(3));
        }

        [Test]
        public async Task GetMixesAsync_filters_by_genre_case_insensitive()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Artist A", "Track 1")),
                MakeMix("2", "dnb", ("Artist B", "Track 2")),
                MakeMix("3", "breaks", ("Artist C", "Track 3")),
            };

            var page = await InvokeMixesAsync(BuildSut(mixes), "Breaks", 1, 20);

            Assert.That(page.Total, Is.EqualTo(2));
            Assert.That(page.Items.Select(m => m.Id), Is.EquivalentTo(new[] { "1", "3" }));
        }

        [Test]
        public async Task GetMixesAsync_genre_filter_returns_empty_when_no_match()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Artist A", "Track 1")),
            };

            var page = await InvokeMixesAsync(BuildSut(mixes), "techno", 1, 20);

            Assert.That(page.Total, Is.EqualTo(0));
        }

        [Test]
        public async Task GetMixesAsync_normalises_genre_alias()
        {
            var mixes = new[]
            {
                MakeMix("1", "deep-house", ("Artist A", "Track 1")),
                MakeMix("2", "dnb", ("Artist B", "Track 2")),
            };

            var page = await InvokeMixesAsync(BuildSut(mixes), "deephouse", 1, 20);

            Assert.That(page.Total, Is.EqualTo(1));
            Assert.That(page.Items[0].Id, Is.EqualTo("1"));
        }

        [Test]
        public async Task GetMixesAsync_returns_400_for_invalid_page()
        {
            IActionResult result = await BuildSut(Array.Empty<Mix>())
                .GetMixesAsync(null, 0, 20, CancellationToken.None);

            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        }

        [Test]
        public async Task GetMixesAsync_returns_400_for_invalid_page_size()
        {
            IActionResult result = await BuildSut(Array.Empty<Mix>())
                .GetMixesAsync(null, 1, 201, CancellationToken.None);

            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        }

        // ── GET /api/catalog/artists ──────────────────────────────────────────
        [Test]
        public async Task GetArtistsAsync_returns_all_names_sorted_alphabetically()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Zinc", "Track 1")),
                MakeMix("2", "dnb", ("Anu", "Track 2")),
                MakeMix("3", "house", ("Calibre", "Track 3")),
            };

            string[] artists = await InvokeArtistsAsync(BuildSut(mixes));

            Assert.That(artists, Is.EqualTo(new[] { "Anu", "Calibre", "Zinc" }));
        }

        [Test]
        public async Task GetArtistsAsync_deduplicates_artist_across_mixes_and_genres()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Shy One", "Alpha")),
                MakeMix("2", "dnb", ("Shy One", "Beta")),
            };

            string[] artists = await InvokeArtistsAsync(BuildSut(mixes));

            Assert.That(artists, Is.EqualTo(new[] { "Shy One" }));
        }

        [Test]
        public async Task GetArtistsAsync_returns_empty_when_no_mixes()
        {
            string[] artists = await InvokeArtistsAsync(BuildSut(Array.Empty<Mix>()));

            Assert.That(artists, Is.Empty);
        }

        [Test]
        public async Task GetArtistsAsync_deduplicates_case_insensitively()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("shy one", "Track 1")),
                MakeMix("2", "dnb", ("Shy One", "Track 2")),
            };

            string[] artists = await InvokeArtistsAsync(BuildSut(mixes));

            Assert.That(artists, Has.Length.EqualTo(1));
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

            var page = await InvokeMixesByArtistAsync(BuildSut(mixes), "Shy One");

            Assert.That(page.Items.Select(m => m.Id), Is.EquivalentTo(new[] { "1", "3" }));
        }

        [Test]
        public async Task GetMixesByArtistAsync_is_case_insensitive()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Shy One", "Track 1")),
                MakeMix("2", "dnb", ("Calibre", "Track 2")),
            };

            var page = await InvokeMixesByArtistAsync(BuildSut(mixes), "shy one");

            Assert.That(page.Items, Has.Length.EqualTo(1));
            Assert.That(page.Items[0].Id, Is.EqualTo("1"));
        }

        [Test]
        public async Task GetMixesByArtistAsync_returns_404_when_artist_not_found()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Shy One", "Track 1")),
            };

            IActionResult result = await BuildSut(mixes).GetMixesByArtistAsync("Unknown Artist", 1, 20, CancellationToken.None);

            Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
        }

        [Test]
        public async Task GetMixesByArtistAsync_returns_mix_once_even_with_multiple_tracks_by_artist()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Shy One", "Track 1"), ("Shy One", "Track 2")),
            };

            var page = await InvokeMixesByArtistAsync(BuildSut(mixes), "Shy One");

            Assert.That(page.Items, Has.Length.EqualTo(1));
        }

        // ── GET /api/catalog/mixes/{slug} ────────────────────────────────────
        [Test]
        public async Task GetMixBySlugAsync_returns_mix_matching_url_slug()
        {
            var mixes = new[]
            {
                MakeMixWithUrl("1", "https://soundcloud.com/changsta/fault-lines", "breaks", ("Artist A", "Track 1")),
                MakeMixWithUrl("2", "https://soundcloud.com/changsta/deep-dive", "dnb", ("Artist B", "Track 2")),
            };

            IActionResult result = await BuildSut(mixes).GetMixBySlugAsync("fault-lines", CancellationToken.None);

            var mix = (Mix)((OkObjectResult)result).Value!;
            Assert.That(mix.Id, Is.EqualTo("1"));
        }

        [Test]
        public async Task GetMixBySlugAsync_returns_404_when_no_match()
        {
            var mixes = new[]
            {
                MakeMixWithUrl("1", "https://soundcloud.com/changsta/fault-lines", "breaks", ("Artist A", "Track 1")),
            };

            IActionResult result = await BuildSut(mixes).GetMixBySlugAsync("unknown-mix", CancellationToken.None);

            Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
        }

        [Test]
        public async Task GetMixBySlugAsync_slug_matching_is_case_insensitive()
        {
            var mixes = new[]
            {
                MakeMixWithUrl("1", "https://soundcloud.com/changsta/fault-lines", "breaks", ("Artist A", "Track 1")),
            };

            IActionResult result = await BuildSut(mixes).GetMixBySlugAsync("FAULT-LINES", CancellationToken.None);

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
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
        private static async Task<CatalogPage<GenreEntry>> InvokeCatalogAsync(
            MixCatalogController sut,
            string? genre,
            int page,
            int pageSize)
        {
            IActionResult result = await sut.GetCatalogAsync(genre, page, pageSize, CancellationToken.None);
            return (CatalogPage<GenreEntry>)((OkObjectResult)result).Value!;
        }

        private static async Task<string[]> InvokeGenresAsync(MixCatalogController sut)
        {
            IActionResult result = await sut.GetGenresAsync(CancellationToken.None);
            var value = (GenresResponse)((OkObjectResult)result).Value!;
            return value.Genres;
        }

        private static async Task<CatalogPage<Mix>> InvokeMixesAsync(
            MixCatalogController sut,
            string? genre,
            int page,
            int pageSize)
        {
            IActionResult result = await sut.GetMixesAsync(genre, page, pageSize, CancellationToken.None);
            return (CatalogPage<Mix>)((OkObjectResult)result).Value!;
        }

        private static async Task<string[]> InvokeArtistsAsync(MixCatalogController sut)
        {
            IActionResult result = await sut.GetArtistsAsync(CancellationToken.None);
            var value = (ArtistNamesResponse)((OkObjectResult)result).Value!;
            return value.Artists;
        }

        private static async Task<CatalogPage<Mix>> InvokeMixesByArtistAsync(MixCatalogController sut, string name)
        {
            IActionResult result = await sut.GetMixesByArtistAsync(name, 1, 20, CancellationToken.None);
            return (CatalogPage<Mix>)((OkObjectResult)result).Value!;
        }

        private static MixCatalogController BuildSut(IReadOnlyList<Mix> mixes)
        {
            return new MixCatalogController(
                new StubMixCatalogueProvider(mixes),
                new StubCatalogFlushUseCase(),
                new StubDeleteMixUseCase(),
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
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

        private static Mix MakeMixWithUrl(string id, string url, string genre, params (string Artist, string Title)[] tracks)
        {
            return new Mix
            {
                Id = id,
                Title = $"Mix {id}",
                Url = url,
                Genre = genre,
                Energy = "high",
                Tracklist = tracks
                    .Select(t => new Track { Artist = t.Artist, Title = t.Title })
                    .ToArray(),
            };
        }

        private sealed class StubCatalogFlushUseCase : ICatalogFlushUseCase
        {
            public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private sealed class StubDeleteMixUseCase : IDeleteMixUseCase
        {
            public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken) => Task.FromResult(false);
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
