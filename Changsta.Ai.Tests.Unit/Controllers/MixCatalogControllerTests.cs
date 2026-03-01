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
        [Test]
        public async Task GetCatalogAsync_groups_artists_under_genre()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks", ("Shy One", "Track S")),
                MakeMix("2", "breaks", ("Anu", "Track A")),
                MakeMix("3", "dnb", ("Zinc", "Track Z")),
            };

            var sut = BuildSut(mixes);

            var page = await InvokeAsync(sut, null, 1, 20);

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

            var sut = BuildSut(mixes);

            var page = await InvokeAsync(sut, "Breaks", 1, 20);

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

            var sut = BuildSut(mixes);

            var page = await InvokeAsync(sut, "deephouse", 1, 20);

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

            var sut = BuildSut(mixes);

            var page = await InvokeAsync(sut, "breaks", 1, 20);

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

            var sut = BuildSut(mixes);

            var page = await InvokeAsync(sut, "breaks", 1, 20);

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

            var sut = BuildSut(mixes);

            var page = await InvokeAsync(sut, null, 2, 2);

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

            var sut = BuildSut(mixes);

            var page = await InvokeAsync(sut, null, 99, 20);

            Assert.That(page.Total, Is.EqualTo(2));
            Assert.That(page.Items.Any(), Is.False);
        }

        [Test]
        public async Task GetCatalogAsync_returns_400_for_page_less_than_1()
        {
            var sut = BuildSut(Array.Empty<Mix>());

            IActionResult result = await sut.GetCatalogAsync(null, 0, 20, CancellationToken.None);

            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        }

        [Test]
        public async Task GetCatalogAsync_returns_400_for_page_size_less_than_1()
        {
            var sut = BuildSut(Array.Empty<Mix>());

            IActionResult result = await sut.GetCatalogAsync(null, 1, 0, CancellationToken.None);

            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        }

        [Test]
        public async Task GetCatalogAsync_returns_400_for_page_size_over_100()
        {
            var sut = BuildSut(Array.Empty<Mix>());

            IActionResult result = await sut.GetCatalogAsync(null, 1, 101, CancellationToken.None);

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

            var sut = BuildSut(mixes);

            var page = await InvokeAsync(sut, null, 1, 20);

            Assert.That(page.Total, Is.EqualTo(2));
            Assert.That(page.Items[0].Artists, Has.Length.EqualTo(2));
        }

        [Test]
        public async Task GetCatalogAsync_returns_correct_total_pages()
        {
            Mix[] mixes = Enumerable.Range(1, 47)
                .Select(i => MakeMix(i.ToString(), $"genre-{i:D3}", ($"Artist {i}", "Track 1")))
                .ToArray();

            var sut = BuildSut(mixes);

            var page = await InvokeAsync(sut, null, 1, 20);

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

            var sut = BuildSut(mixes);

            var page = await InvokeAsync(sut, null, 1, 20);

            Assert.That(page.Total, Is.EqualTo(1));
            Assert.That(page.Items[0].Genre, Is.EqualTo("dnb"));
        }

        private static async Task<MixCatalogController.CatalogPage> InvokeAsync(
            MixCatalogController sut,
            string? genre,
            int page,
            int pageSize)
        {
            IActionResult result = await sut.GetCatalogAsync(genre, page, pageSize, CancellationToken.None);
            return (MixCatalogController.CatalogPage)((OkObjectResult)result).Value!;
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
