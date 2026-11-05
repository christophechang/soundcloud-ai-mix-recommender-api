using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Interface.Api.Controllers;
using Changsta.Ai.Interface.Api.ViewModels;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Changsta.Ai.Tests.Unit.Controllers
{
    [TestFixture]
    public sealed class CatalogStatsControllerTests
    {
        [Test]
        public async Task GetStatsAsync_returns_204_when_provider_returns_null()
        {
            CatalogStatsController sut = BuildSut(null);

            IActionResult result = await sut.GetStatsAsync(CancellationToken.None);

            result.Should().BeOfType<NoContentResult>();
            sut.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        }

        [Test]
        public async Task GetStatsAsync_returns_items_with_catalogue_mix_ids()
        {
            CatalogStatsController sut = BuildSut(new[]
            {
                new MixEngagement { TrackId = 2432119239, Plays = 1204, Likes = 87 },
                new MixEngagement { TrackId = 1000000003 },
            });

            IActionResult result = await sut.GetStatsAsync(CancellationToken.None);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var response = ok.Value.Should().BeOfType<CatalogStatsResponse>().Subject;
            response.Items.Should().HaveCount(2);
            response.Items[0].Id.Should().Be("tag:soundcloud,2010:tracks/2432119239");
            response.Items[0].Plays.Should().Be(1204);
            response.Items[0].Likes.Should().Be(87);
            response.Items[1].Id.Should().Be("tag:soundcloud,2010:tracks/1000000003");
            response.Items[1].Plays.Should().BeNull();
            response.Items[1].Likes.Should().BeNull();
            sut.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        }

        [Test]
        public void Stats_route_is_a_literal_segment_under_api_catalog()
        {
            var route = (RouteAttribute)System.Attribute.GetCustomAttribute(typeof(CatalogStatsController), typeof(RouteAttribute)) !;
            var get = (HttpGetAttribute)System.Attribute.GetCustomAttribute(
                typeof(CatalogStatsController).GetMethod(nameof(CatalogStatsController.GetStatsAsync)) !,
                typeof(HttpGetAttribute)) !;

            route.Template.Should().Be("api/catalog");
            get.Template.Should().Be("stats");
        }

        private static CatalogStatsController BuildSut(IReadOnlyList<MixEngagement>? stats) =>
            new CatalogStatsController(new StubProvider(stats))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };

        private sealed class StubProvider : IMixEngagementProvider
        {
            private readonly IReadOnlyList<MixEngagement>? _stats;

            public StubProvider(IReadOnlyList<MixEngagement>? stats)
            {
                _stats = stats;
            }

            public Task<IReadOnlyList<MixEngagement>?> GetAllAsync(CancellationToken cancellationToken) =>
                Task.FromResult(_stats);
        }
    }
}
