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
        public async Task GetCatalogAsync_returns_all_mixes_when_no_genre_filter()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks"),
                MakeMix("2", "dnb"),
                MakeMix("3", "deep-house"),
            };

            var sut = BuildSut(mixes);

            var page = await InvokeAsync(sut, null, 1, 20);

            Assert.That(page.Total, Is.EqualTo(3));
        }

        [Test]
        public async Task GetCatalogAsync_filters_by_genre_case_insensitive()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks"),
                MakeMix("2", "dnb"),
                MakeMix("3", "breaks"),
            };

            var sut = BuildSut(mixes);

            var page = await InvokeAsync(sut, "Breaks", 1, 20);

            Assert.That(page.Total, Is.EqualTo(2));
            Assert.That(page.Items.All(m => m.Genre == "breaks"), Is.True);
        }

        [Test]
        public async Task GetCatalogAsync_normalises_genre_alias()
        {
            var mixes = new[]
            {
                MakeMix("1", "deep-house"),
                MakeMix("2", "dnb"),
            };

            var sut = BuildSut(mixes);

            var page = await InvokeAsync(sut, "deephouse", 1, 20);

            Assert.That(page.Total, Is.EqualTo(1));
        }

        [Test]
        public async Task GetCatalogAsync_returns_correct_page()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks"),
                MakeMix("2", "breaks"),
                MakeMix("3", "breaks"),
                MakeMix("4", "breaks"),
                MakeMix("5", "breaks"),
            };

            var sut = BuildSut(mixes);

            var page = await InvokeAsync(sut, null, 2, 2);

            Assert.That(page.Items.Select(m => m.Id), Is.EqualTo(new[] { "3", "4" }));
        }

        [Test]
        public async Task GetCatalogAsync_returns_empty_items_beyond_last_page()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks"),
                MakeMix("2", "breaks"),
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
        public async Task GetCatalogAsync_returns_correct_total_pages()
        {
            Mix[] mixes = Enumerable.Range(1, 47)
                .Select(i => MakeMix(i.ToString(), "breaks"))
                .ToArray();

            var sut = BuildSut(mixes);

            var page = await InvokeAsync(sut, null, 1, 20);

            Assert.That(page.TotalPages, Is.EqualTo(3));
        }

        [Test]
        public async Task GetCatalogAsync_genre_filter_with_paging_returns_correct_slice()
        {
            var mixes = new[]
            {
                MakeMix("1", "breaks"),
                MakeMix("2", "dnb"),
                MakeMix("3", "breaks"),
                MakeMix("4", "breaks"),
                MakeMix("5", "dnb"),
            };

            var sut = BuildSut(mixes);

            var page = await InvokeAsync(sut, "breaks", 2, 2);

            Assert.That(page.Total, Is.EqualTo(3));
            Assert.That(page.Items.Select(m => m.Id), Is.EqualTo(new[] { "4" }));
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

        private static Mix MakeMix(string id, string genre)
        {
            return new Mix
            {
                Id = id,
                Title = $"Mix {id}",
                Url = $"https://sc.test/mix-{id}",
                Genre = genre,
                Energy = "high",
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
