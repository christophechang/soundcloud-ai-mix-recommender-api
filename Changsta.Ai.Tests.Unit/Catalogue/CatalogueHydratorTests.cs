using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.Azure.Catalogue;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Catalogue
{
    [TestFixture]
    public sealed class CatalogueHydratorTests
    {
        [Test]
        public async Task Hydrates_a_missing_intro_from_the_description_and_reports_the_change()
        {
            var hydrator = new CatalogueHydrator(new StubMoodWeightResolver(), NullLogger.Instance);

            CatalogueHydrationResult result = await hydrator.HydrateAsync(
                new[] { MakeMix("1", description: "A rolling deep sub-bass journey.\nTracklist\n1. Calibre - Pillow Dub") },
                CancellationToken.None);

            result.Mixes[0].Intro.Should().NotBeNull();
            result.Changed.Should().BeTrue();
        }

        [Test]
        public async Task Leaves_an_existing_intro_untouched()
        {
            var hydrator = new CatalogueHydrator(new StubMoodWeightResolver(), NullLogger.Instance);

            Mix mix = MakeMix("1", description: "Something else entirely.\nTracklist\n1. Calibre - Pillow Dub") with { Intro = "Already set." };

            CatalogueHydrationResult result = await hydrator.HydrateAsync(
                new[] { mix },
                CancellationToken.None);

            result.Mixes[0].Intro.Should().Be("Already set.");
        }

        [Test]
        public async Task Reports_no_change_when_nothing_could_be_derived()
        {
            var hydrator = new CatalogueHydrator(new StubMoodWeightResolver(), NullLogger.Instance);

            CatalogueHydrationResult result = await hydrator.HydrateAsync(
                Array.Empty<Mix>(),
                CancellationToken.None);

            result.Changed.Should().BeFalse();
            result.Mixes.Should().BeEmpty();
        }

        [Test]
        public async Task Scores_warmth_using_the_resolved_mood_weights()
        {
            var resolver = new StubMoodWeightResolver(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["warm"] = 1.0,
            });

            var hydrator = new CatalogueHydrator(resolver, NullLogger.Instance);

            CatalogueHydrationResult result = await hydrator.HydrateAsync(
                new[] { MakeMix("1") with { Moods = new[] { "warm" } } },
                CancellationToken.None);

            resolver.Called.Should().BeTrue();
            result.Mixes[0].Warmth.Should().NotBeNull();
            result.Changed.Should().BeTrue();
        }

        private static Mix MakeMix(string id, string? description = null) => new Mix
        {
            Id = id,
            Title = $"Mix {id}",
            Url = $"https://soundcloud.com/changsta/{id}",
            Description = description,
            Genre = "dnb",
            Energy = "mid",
            Tracklist = Array.Empty<Track>(),
        };

        private sealed class StubMoodWeightResolver : IMoodWeightResolver
        {
            private readonly IReadOnlyDictionary<string, double> _weights;

            public StubMoodWeightResolver(IReadOnlyDictionary<string, double>? weights = null) =>
                _weights = weights ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            public bool Called { get; private set; }

            public Task<IReadOnlyDictionary<string, double>> ResolveAsync(
                IReadOnlyList<Mix> mixes,
                CancellationToken cancellationToken)
            {
                Called = true;
                return Task.FromResult(_weights);
            }
        }
    }
}
