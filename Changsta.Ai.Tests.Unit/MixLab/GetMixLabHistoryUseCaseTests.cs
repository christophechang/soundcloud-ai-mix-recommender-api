using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.BusinessProcesses.MixLab;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Infrastructure.Services.Azure.MixLab;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    [TestFixture]
    public sealed class GetMixLabHistoryUseCaseTests
    {
        [Test]
        public async Task GetAsync_no_history_blob_returns_null()
        {
            (GetMixLabHistoryUseCase sut, _) = BuildSut();

            MixLabHistorySnapshot? snapshot = await sut.GetAsync(CancellationToken.None);

            snapshot.Should().BeNull();
        }

        [Test]
        public async Task GetAsync_existing_history_returns_content_and_etag()
        {
            (GetMixLabHistoryUseCase sut, BlobMixLabHistoryStore store) = BuildSut();
            string etag = await store.PutAsync("{\"concepts\":[]}", ifMatchETag: null, CancellationToken.None);

            MixLabHistorySnapshot? snapshot = await sut.GetAsync(CancellationToken.None);

            snapshot.Should().NotBeNull();
            snapshot!.Content.Should().Be("{\"concepts\":[]}");
            snapshot.ETag.Should().Be(etag);
        }

        private static (GetMixLabHistoryUseCase Sut, BlobMixLabHistoryStore Store) BuildSut()
        {
            var gateway = new FakeMixLabBlobGateway();
            var store = new BlobMixLabHistoryStore(gateway);
            var sut = new GetMixLabHistoryUseCase(store);
            return (sut, store);
        }
    }
}
