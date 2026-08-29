using System.Collections.Generic;
using System.Security;
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

            var sut = new CatalogFlushUseCase(invalidator, provider, NullLogger<CatalogFlushUseCase>.Instance);

            await sut.FlushAsync(CancellationToken.None);

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

            var sut = new CatalogFlushUseCase(invalidator, provider, NullLogger<CatalogFlushUseCase>.Instance);
            await sut.FlushAsync(CancellationToken.None);

            Assert.That(invalidateVersionAtWarmup, Is.EqualTo(1), "Invalidate must be called before GetLatestAsync.");
        }

        [Test]
        public async Task FlushAsync_keeps_invalidation_when_rewarm_hits_security_validation()
        {
            var invalidator = new SpyCatalogCacheInvalidator();
            var provider = new SpyMixCatalogueProvider
            {
                OnGetLatest = () => throw new SecurityException("Invalid assembly public key."),
            };

            var sut = new CatalogFlushUseCase(invalidator, provider, NullLogger<CatalogFlushUseCase>.Instance);

            await sut.FlushAsync(CancellationToken.None);

            Assert.That(invalidator.InvalidateCallCount, Is.EqualTo(1));
            Assert.That(provider.GetLatestCallCount, Is.EqualTo(1));
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
