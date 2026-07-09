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
    public sealed class PutMixLabHistoryUseCaseTests
    {
        [Test]
        public async Task PutAsync_first_write_without_if_match_succeeds()
        {
            (PutMixLabHistoryUseCase sut, _) = BuildSut();

            PutMixLabHistoryResult result = await sut.PutAsync("{\"concepts\":[]}", ifMatchETag: null, CancellationToken.None);

            result.Outcome.Should().Be(PutMixLabHistoryResult.PutOutcome.Written);
            result.ETag.Should().NotBeNullOrEmpty();
        }

        [Test]
        public async Task PutAsync_matching_if_match_succeeds_and_returns_new_etag()
        {
            (PutMixLabHistoryUseCase sut, _) = BuildSut();
            PutMixLabHistoryResult first = await sut.PutAsync("{\"concepts\":[]}", ifMatchETag: null, CancellationToken.None);

            PutMixLabHistoryResult second = await sut.PutAsync("{\"concepts\":[1]}", first.ETag, CancellationToken.None);

            second.Outcome.Should().Be(PutMixLabHistoryResult.PutOutcome.Written);
            second.ETag.Should().NotBe(first.ETag);
        }

        [Test]
        public async Task PutAsync_stale_if_match_is_precondition_failed()
        {
            (PutMixLabHistoryUseCase sut, _) = BuildSut();
            await sut.PutAsync("{\"concepts\":[]}", ifMatchETag: null, CancellationToken.None);

            PutMixLabHistoryResult result = await sut.PutAsync("{\"concepts\":[1]}", "stale-etag", CancellationToken.None);

            result.Outcome.Should().Be(PutMixLabHistoryResult.PutOutcome.PreconditionFailed);
        }

        [Test]
        public async Task PutAsync_missing_if_match_when_blob_exists_is_precondition_failed()
        {
            // Per IMixLabHistoryStore.PutAsync's contract, a null ifMatchETag is create-only: the
            // underlying store rejects it as a MixLabConcurrencyException once a document already
            // exists, so this use case maps that the same way it maps a stale ETag.
            (PutMixLabHistoryUseCase sut, _) = BuildSut();
            await sut.PutAsync("{\"concepts\":[]}", ifMatchETag: null, CancellationToken.None);

            PutMixLabHistoryResult result = await sut.PutAsync("{\"concepts\":[1]}", ifMatchETag: null, CancellationToken.None);

            result.Outcome.Should().Be(PutMixLabHistoryResult.PutOutcome.PreconditionFailed);
        }

        [Test]
        public async Task PutAsync_malformed_json_body_is_invalid_json()
        {
            (PutMixLabHistoryUseCase sut, _) = BuildSut();

            PutMixLabHistoryResult result = await sut.PutAsync("{not json", ifMatchETag: null, CancellationToken.None);

            result.Outcome.Should().Be(PutMixLabHistoryResult.PutOutcome.InvalidJson);
        }

        private static (PutMixLabHistoryUseCase Sut, BlobMixLabHistoryStore Store) BuildSut()
        {
            var gateway = new FakeMixLabBlobGateway();
            var store = new BlobMixLabHistoryStore(gateway);
            var sut = new PutMixLabHistoryUseCase(store);
            return (sut, store);
        }
    }
}
