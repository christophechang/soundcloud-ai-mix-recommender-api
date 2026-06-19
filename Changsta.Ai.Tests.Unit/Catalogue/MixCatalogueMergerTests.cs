using System;
using System.Collections.Generic;
using System.Linq;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.Azure.Catalogue;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Catalogue
{
    [TestFixture]
    public sealed class MixCatalogueMergerTests
    {
        [Test]
        public void Merge_new_rss_discoveries_come_first_then_blob_entries()
        {
            var blob = new[] { MakeMix("b1", "https://sc.test/old", genre: "house") };
            var rss = new[]
            {
                MakeMix("r1", "https://sc.test/new", genre: "dnb"),
                MakeMix("b1", "https://sc.test/old", genre: "house"),
            };

            IReadOnlyList<Mix> merged = MixCatalogueMerger.Merge(blob, rss);

            merged.Select(m => m.Url).Should().Equal("https://sc.test/new", "https://sc.test/old");
        }

        [Test]
        public void Merge_excludes_metadata_only_rss_discoveries()
        {
            var blob = Array.Empty<Mix>();
            var rss = new[]
            {
                new Mix { Id = "meta", Title = "Meta", Url = "https://sc.test/meta", Genre = string.Empty, Energy = string.Empty },
            };

            MixCatalogueMerger.Merge(blob, rss).Should().BeEmpty();
        }

        [Test]
        public void Merge_syncs_schema_from_rss_when_description_changes_and_rss_has_schema()
        {
            var blob = new[] { MakeMix("1", "https://sc.test/m", genre: "house", description: "old") };
            var rss = new[] { MakeMix("1", "https://sc.test/m", genre: "dnb", description: "new") };

            Mix merged = MixCatalogueMerger.Merge(blob, rss).Single();

            merged.Genre.Should().Be("dnb");
            merged.Description.Should().Be("new");
        }

        [Test]
        public void CountNewDiscoveries_ignores_blob_urls_and_metadata_only()
        {
            var blob = new[] { MakeMix("1", "https://sc.test/a", genre: "house") };
            var rss = new[]
            {
                MakeMix("1", "https://sc.test/a", genre: "house"),
                MakeMix("2", "https://sc.test/b", genre: "dnb"),
            };

            MixCatalogueMerger.CountNewDiscoveries(blob, rss).Should().Be(1);
        }

        [Test]
        public void CountUpdatedEntries_counts_title_or_description_changes()
        {
            var blob = new[] { MakeMix("1", "https://sc.test/a", genre: "house", description: "old") };
            var rss = new[] { MakeMix("1", "https://sc.test/a", genre: "house", description: "new") };

            MixCatalogueMerger.CountUpdatedEntries(blob, rss).Should().Be(1);
        }

        private static Mix MakeMix(string id, string url, string genre, string? description = null) => new Mix
        {
            Id = id,
            Title = $"Mix {id}",
            Url = url,
            Genre = genre,
            Energy = "mid",
            Description = description,
            BpmMin = 120,
            BpmMax = 124,
        };
    }
}
