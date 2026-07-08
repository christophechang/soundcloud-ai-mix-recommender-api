using System;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Exceptions;
using Changsta.Ai.Infrastructure.Services.Azure.MixLab;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    [TestFixture]
    public sealed class BlobMixLabHistoryStoreTests
    {
        [Test]
        public async Task GetAsync_no_blob_returns_null()
        {
            var sut = new BlobMixLabHistoryStore(new FakeMixLabBlobGateway());

            var snapshot = await sut.GetAsync(CancellationToken.None);

            snapshot.Should().BeNull();
        }

        [Test]
        public async Task PutAsync_then_GetAsync_round_trips_content_and_etag()
        {
            var sut = new BlobMixLabHistoryStore(new FakeMixLabBlobGateway());

            string firstETag = await sut.PutAsync("{\"concepts\":[]}", ifMatchETag: null, CancellationToken.None);

            var snapshot = await sut.GetAsync(CancellationToken.None);

            snapshot.Should().NotBeNull();
            snapshot!.Content.Should().Be("{\"concepts\":[]}");
            snapshot.ETag.Should().Be(firstETag);
        }

        [Test]
        public async Task PutAsync_wrong_if_match_etag_throws_concurrency_exception()
        {
            var sut = new BlobMixLabHistoryStore(new FakeMixLabBlobGateway());

            await sut.PutAsync("{\"concepts\":[]}", ifMatchETag: null, CancellationToken.None);

            Func<Task> act = () => sut.PutAsync("{\"concepts\":[1]}", ifMatchETag: "stale-etag", CancellationToken.None);

            await act.Should().ThrowAsync<MixLabConcurrencyException>();
        }

        [Test]
        public async Task PutAsync_correct_if_match_etag_succeeds_and_returns_new_etag()
        {
            var sut = new BlobMixLabHistoryStore(new FakeMixLabBlobGateway());

            string firstETag = await sut.PutAsync("{\"concepts\":[]}", ifMatchETag: null, CancellationToken.None);
            string secondETag = await sut.PutAsync("{\"concepts\":[1]}", ifMatchETag: firstETag, CancellationToken.None);

            var snapshot = await sut.GetAsync(CancellationToken.None);

            secondETag.Should().NotBe(firstETag);
            snapshot!.ETag.Should().Be(secondETag);
            snapshot.Content.Should().Be("{\"concepts\":[1]}");
        }
    }
}
