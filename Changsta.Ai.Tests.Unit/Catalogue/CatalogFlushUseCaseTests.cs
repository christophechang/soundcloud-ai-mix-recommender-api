using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.BusinessProcesses.Catalogue;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Catalogue
{
    [TestFixture]
    public sealed class CatalogFlushUseCaseTests
    {
        [Test]
        public async Task FlushAsync_invalidates_cache_then_rewarms()
        {
            var invalidator = new SpyCatalogCacheInvalidator();
            var provider = new SpyMixCatalogueProvider();

            var sut = CreateSut(invalidator, provider);

            await sut.FlushAsync(CancellationToken.None);
            await sut.RewarmTask!;

            Assert.That(invalidator.InvalidateCallCount, Is.EqualTo(1));
            Assert.That(provider.GetLatestCallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task FlushAsync_invalidates_before_rewarming()
        {
            var invalidator = new SpyCatalogCacheInvalidator();
            var provider = new SpyMixCatalogueProvider();
            int invalidateVersionAtWarmup = -1;

            provider.OnGetLatest = () => invalidateVersionAtWarmup = invalidator.InvalidateCallCount;

            var sut = CreateSut(invalidator, provider);
            await sut.FlushAsync(CancellationToken.None);
            await sut.RewarmTask!;

            Assert.That(invalidateVersionAtWarmup, Is.EqualTo(1), "Invalidate must be called before GetLatestAsync.");
        }

        [Test]
        public async Task FlushAsync_returns_before_the_rewarm_finishes()
        {
            var invalidator = new SpyCatalogCacheInvalidator();
            var load = new TaskCompletionSource<IReadOnlyList<Mix>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new SpyMixCatalogueProvider { Result = load.Task };

            var sut = CreateSut(invalidator, provider);

            Task flush = sut.FlushAsync(CancellationToken.None);

            Assert.That(flush.IsCompletedSuccessfully, Is.True, "The flush must not wait for the rewarm.");
            Assert.That(invalidator.InvalidateCallCount, Is.EqualTo(1));
            Assert.That(sut.RewarmTask!.IsCompleted, Is.False);

            load.SetResult(System.Array.Empty<Mix>());
            await sut.RewarmTask;
            await flush;
        }

        [Test]
        public async Task FlushAsync_rewarm_ignores_the_request_token()
        {
            var invalidator = new SpyCatalogCacheInvalidator();
            var provider = new SpyMixCatalogueProvider();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var sut = CreateSut(invalidator, provider);
            await sut.FlushAsync(cts.Token);
            await sut.RewarmTask!;

            Assert.That(provider.GetLatestCallCount, Is.EqualTo(1));
            Assert.That(provider.LastToken.CanBeCanceled, Is.False, "A dropped request must not cancel the rewarm.");
        }

        [Test]
        public async Task FlushAsync_rewarm_failure_is_swallowed()
        {
            var invalidator = new SpyCatalogCacheInvalidator();
            var provider = new SpyMixCatalogueProvider
            {
                Result = Task.FromException<IReadOnlyList<Mix>>(new System.InvalidOperationException("blob down")),
            };

            var sut = CreateSut(invalidator, provider);
            await sut.FlushAsync(CancellationToken.None);

            Assert.That(async () => await sut.RewarmTask!, Throws.Nothing);
            Assert.That(invalidator.InvalidateCallCount, Is.EqualTo(1));
        }

        private static CatalogFlushUseCase CreateSut(ICatalogCacheInvalidator invalidator, IMixCatalogueProvider provider) =>
            new CatalogFlushUseCase(invalidator, provider, NullLogger<CatalogFlushUseCase>.Instance);

        private sealed class SpyCatalogCacheInvalidator : ICatalogCacheInvalidator
        {
            public int InvalidateCallCount { get; private set; }

            public int Version => InvalidateCallCount;

            public void Invalidate() => InvalidateCallCount++;
        }

        private sealed class SpyMixCatalogueProvider : IMixCatalogueProvider
        {
            public int GetLatestCallCount { get; private set; }

            public CancellationToken LastToken { get; private set; }

            public System.Action? OnGetLatest { get; set; }

            public Task<IReadOnlyList<Mix>>? Result { get; set; }

            public Task<IReadOnlyList<Mix>> GetLatestAsync(int maxItems, CancellationToken cancellationToken)
            {
                GetLatestCallCount++;
                LastToken = cancellationToken;
                OnGetLatest?.Invoke();
                return Result ?? Task.FromResult<IReadOnlyList<Mix>>(System.Array.Empty<Mix>());
            }
        }
    }
}
