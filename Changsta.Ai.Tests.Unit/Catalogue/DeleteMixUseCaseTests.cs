using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.BusinessProcesses.Catalogue;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Catalogue
{
    [TestFixture]
    public sealed class DeleteMixUseCaseTests
    {
        [Test]
        public async Task DeleteAsync_returns_true_and_invalidates_cache_when_mix_found()
        {
            var deleter = new StubCatalogMixDeleter(returns: true);
            var invalidator = new SpyCatalogCacheInvalidator();
            var provider = new SpyMixCatalogueProvider();

            var sut = new DeleteMixUseCase(deleter, invalidator, provider);

            bool result = await sut.DeleteAsync("some-id", CancellationToken.None);

            Assert.That(result, Is.True);
            Assert.That(invalidator.InvalidateCallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task DeleteAsync_rewarms_cache_after_successful_delete()
        {
            var deleter = new StubCatalogMixDeleter(returns: true);
            var invalidator = new SpyCatalogCacheInvalidator();
            var provider = new SpyMixCatalogueProvider();

            var sut = new DeleteMixUseCase(deleter, invalidator, provider);

            await sut.DeleteAsync("some-id", CancellationToken.None);

            Assert.That(provider.GetLatestCallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task DeleteAsync_rewarms_after_invalidate()
        {
            var deleter = new StubCatalogMixDeleter(returns: true);
            var invalidator = new SpyCatalogCacheInvalidator();
            var provider = new SpyMixCatalogueProvider();
            int invalidateVersionAtWarmup = -1;

            provider.OnGetLatest = () => invalidateVersionAtWarmup = invalidator.InvalidateCallCount;

            var sut = new DeleteMixUseCase(deleter, invalidator, provider);
            await sut.DeleteAsync("some-id", CancellationToken.None);

            Assert.That(invalidateVersionAtWarmup, Is.EqualTo(1), "Invalidate must be called before GetLatestAsync.");
        }

        [Test]
        public async Task DeleteAsync_returns_false_and_does_not_invalidate_cache_when_mix_not_found()
        {
            var deleter = new StubCatalogMixDeleter(returns: false);
            var invalidator = new SpyCatalogCacheInvalidator();
            var provider = new SpyMixCatalogueProvider();

            var sut = new DeleteMixUseCase(deleter, invalidator, provider);

            bool result = await sut.DeleteAsync("unknown-id", CancellationToken.None);

            Assert.That(result, Is.False);
            Assert.That(invalidator.InvalidateCallCount, Is.EqualTo(0));
            Assert.That(provider.GetLatestCallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task DeleteAsync_passes_id_to_deleter()
        {
            var deleter = new StubCatalogMixDeleter(returns: true);
            var invalidator = new SpyCatalogCacheInvalidator();
            var provider = new SpyMixCatalogueProvider();

            var sut = new DeleteMixUseCase(deleter, invalidator, provider);

            await sut.DeleteAsync("target-id", CancellationToken.None);

            Assert.That(deleter.LastIdReceived, Is.EqualTo("target-id"));
        }

        private sealed class StubCatalogMixDeleter : ICatalogMixDeleter
        {
            private readonly bool _returns;

            public StubCatalogMixDeleter(bool returns) => _returns = returns;

            public string? LastIdReceived { get; private set; }

            public Task<bool> DeleteByIdAsync(string id, CancellationToken cancellationToken)
            {
                LastIdReceived = id;
                return Task.FromResult(_returns);
            }
        }

        private sealed class SpyCatalogCacheInvalidator : ICatalogCacheInvalidator
        {
            public int InvalidateCallCount { get; private set; }

            public int Version => InvalidateCallCount;

            public void Invalidate() => InvalidateCallCount++;
        }

        private sealed class SpyMixCatalogueProvider : IMixCatalogueProvider
        {
            public int GetLatestCallCount { get; private set; }

            public System.Action? OnGetLatest { get; set; }

            public Task<IReadOnlyList<Mix>> GetLatestAsync(int maxItems, CancellationToken cancellationToken)
            {
                GetLatestCallCount++;
                OnGetLatest?.Invoke();
                return Task.FromResult<IReadOnlyList<Mix>>(System.Array.Empty<Mix>());
            }
        }
    }
}
