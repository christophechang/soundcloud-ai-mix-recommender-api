using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Ai;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.Azure.Catalogue;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Catalogue
{
    [TestFixture]
    public sealed class MoodWeightResolverTests
    {
        [Test]
        public async Task Overlays_stored_enriched_weights_on_the_configured_base()
        {
            var resolver = new MoodWeightResolver(
                Base(("dark", 0.2)),
                new StubEnrichmentRepository(Weights(("rolling", 0.7))),
                new StubEnricher(),
                NullLogger.Instance);

            IReadOnlyDictionary<string, double> weights =
                await resolver.ResolveAsync(Mixes("dark", "rolling"), CancellationToken.None);

            weights["dark"].Should().Be(0.2);
            weights["rolling"].Should().Be(0.7);
        }

        [Test]
        public async Task Enriches_only_the_moods_with_no_weight()
        {
            var enricher = new StubEnricher(Weights(("uplifting", 0.9)));

            var resolver = new MoodWeightResolver(
                Base(("dark", 0.2)),
                new StubEnrichmentRepository(),
                enricher,
                NullLogger.Instance);

            IReadOnlyDictionary<string, double> weights =
                await resolver.ResolveAsync(Mixes("dark", "uplifting"), CancellationToken.None);

            enricher.RequestedMoods.Should().Equal("uplifting");
            weights["uplifting"].Should().Be(0.9);
        }

        [Test]
        public async Task Does_not_call_the_enricher_when_every_mood_is_known()
        {
            var enricher = new StubEnricher();

            var resolver = new MoodWeightResolver(
                Base(("dark", 0.2)),
                new StubEnrichmentRepository(),
                enricher,
                NullLogger.Instance);

            await resolver.ResolveAsync(Mixes("dark"), CancellationToken.None);

            enricher.CallCount.Should().Be(0);
        }

        [Test]
        public async Task Persists_newly_enriched_weights()
        {
            var repository = new StubEnrichmentRepository();

            var resolver = new MoodWeightResolver(
                Base(),
                repository,
                new StubEnricher(Weights(("uplifting", 0.9))),
                NullLogger.Instance);

            await resolver.ResolveAsync(Mixes("uplifting"), CancellationToken.None);

            repository.Written.Should().ContainKey("uplifting");
        }

        [Test]
        public async Task An_enricher_failure_leaves_the_known_weights_usable()
        {
            var resolver = new MoodWeightResolver(
                Base(("dark", 0.2)),
                new StubEnrichmentRepository(),
                new ThrowingEnricher(),
                NullLogger.Instance);

            IReadOnlyDictionary<string, double> weights =
                await resolver.ResolveAsync(Mixes("dark", "uplifting"), CancellationToken.None);

            weights["dark"].Should().Be(0.2);
            weights.ContainsKey("uplifting").Should().BeFalse();
        }

        [Test]
        public async Task A_repository_read_failure_falls_back_to_the_base_weights()
        {
            var resolver = new MoodWeightResolver(
                Base(("dark", 0.2)),
                new ThrowingEnrichmentRepository(),
                new StubEnricher(),
                NullLogger.Instance);

            IReadOnlyDictionary<string, double> weights =
                await resolver.ResolveAsync(Mixes("dark"), CancellationToken.None);

            weights["dark"].Should().Be(0.2);
        }

        [Test]
        public async Task A_persistence_failure_does_not_lose_the_in_memory_scores()
        {
            var resolver = new MoodWeightResolver(
                Base(),
                new ThrowingOnWriteRepository(),
                new StubEnricher(Weights(("uplifting", 0.9))),
                NullLogger.Instance);

            IReadOnlyDictionary<string, double> weights =
                await resolver.ResolveAsync(Mixes("uplifting"), CancellationToken.None);

            weights["uplifting"].Should().Be(0.9);
        }

        [Test]
        public async Task The_null_implementations_resolve_to_the_base_weights_only()
        {
            var resolver = new MoodWeightResolver(
                Base(("dark", 0.2)),
                NullMoodWeightEnrichmentRepository.Instance,
                NullMoodWeightEnricher.Instance,
                NullLogger.Instance);

            IReadOnlyDictionary<string, double> weights =
                await resolver.ResolveAsync(Mixes("dark", "uplifting"), CancellationToken.None);

            weights["dark"].Should().Be(0.2);
            weights.ContainsKey("uplifting").Should().BeFalse();
        }

        private static Dictionary<string, double> Base(params (string Mood, double Weight)[] entries) =>
            Weights(entries);

        private static Dictionary<string, double> Weights(params (string Mood, double Weight)[] entries)
        {
            var dictionary = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach ((string mood, double weight) in entries)
            {
                dictionary[mood] = weight;
            }

            return dictionary;
        }

        private static IReadOnlyList<Mix> Mixes(params string[] moods) => new[]
        {
            new Mix
            {
                Id = "1",
                Title = "Mix 1",
                Url = "https://soundcloud.com/changsta/1",
                Genre = "dnb",
                Energy = "mid",
                Moods = moods,
                Tracklist = Array.Empty<Track>(),
            },
        };

        private sealed class StubEnrichmentRepository : IMoodWeightEnrichmentRepository
        {
            private readonly IReadOnlyDictionary<string, double> _stored;

            public StubEnrichmentRepository(IReadOnlyDictionary<string, double>? stored = null) =>
                _stored = stored ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            public IReadOnlyDictionary<string, double> Written { get; private set; } =
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            public Task<IReadOnlyDictionary<string, double>> ReadAsync(CancellationToken cancellationToken) =>
                Task.FromResult(_stored);

            public Task WriteAsync(IReadOnlyDictionary<string, double> weights, CancellationToken cancellationToken)
            {
                Written = weights;
                return Task.CompletedTask;
            }
        }

        private sealed class ThrowingEnrichmentRepository : IMoodWeightEnrichmentRepository
        {
            public Task<IReadOnlyDictionary<string, double>> ReadAsync(CancellationToken cancellationToken) =>
                throw new InvalidOperationException("enrichment store down");

            public Task WriteAsync(IReadOnlyDictionary<string, double> weights, CancellationToken cancellationToken) =>
                Task.CompletedTask;
        }

        private sealed class ThrowingOnWriteRepository : IMoodWeightEnrichmentRepository
        {
            public Task<IReadOnlyDictionary<string, double>> ReadAsync(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyDictionary<string, double>>(
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));

            public Task WriteAsync(IReadOnlyDictionary<string, double> weights, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("enrichment store read-only");
        }

        private sealed class StubEnricher : IMoodWeightEnricher
        {
            private readonly IReadOnlyDictionary<string, double> _scores;

            public StubEnricher(IReadOnlyDictionary<string, double>? scores = null) =>
                _scores = scores ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            public int CallCount { get; private set; }

            public IReadOnlyList<string> RequestedMoods { get; private set; } = Array.Empty<string>();

            public Task<IReadOnlyDictionary<string, double>> EnrichAsync(
                IReadOnlyDictionary<string, double> existingWeights,
                IReadOnlyList<string> newMoods,
                CancellationToken cancellationToken = default)
            {
                CallCount++;
                RequestedMoods = newMoods;
                return Task.FromResult(_scores);
            }
        }

        private sealed class ThrowingEnricher : IMoodWeightEnricher
        {
            public Task<IReadOnlyDictionary<string, double>> EnrichAsync(
                IReadOnlyDictionary<string, double> existingWeights,
                IReadOnlyList<string> newMoods,
                CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException("AI down");
        }
    }
}
