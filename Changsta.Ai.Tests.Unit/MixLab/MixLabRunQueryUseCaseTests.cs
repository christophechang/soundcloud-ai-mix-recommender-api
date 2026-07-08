using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.BusinessProcesses.MixLab;
using Changsta.Ai.Core.Domain.MixLab;
using Changsta.Ai.Infrastructure.Services.Azure.MixLab;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    [TestFixture]
    public sealed class MixLabRunQueryUseCaseTests
    {
        [Test]
        public async Task ListAsync_null_paging_applies_defaults_and_returns_all_within_default_take()
        {
            (MixLabRunQueryUseCase sut, BlobMixLabRunRepository runs, FakeTimeProvider time) = BuildSut();
            await SeedRunsAsync(runs, time, 3);

            IReadOnlyList<MixLabRunIndexEntry> page = await sut.ListAsync(null, null, CancellationToken.None);

            page.Should().HaveCount(3);
        }

        [Test]
        public async Task ListAsync_honours_take_and_skip()
        {
            (MixLabRunQueryUseCase sut, BlobMixLabRunRepository runs, FakeTimeProvider time) = BuildSut();
            await SeedRunsAsync(runs, time, 3);

            IReadOnlyList<MixLabRunIndexEntry> page = await sut.ListAsync(take: 1, skip: 1, CancellationToken.None);

            page.Should().HaveCount(1);
        }

        [Test]
        public async Task ListAsync_take_below_one_is_clamped_up_to_one()
        {
            (MixLabRunQueryUseCase sut, BlobMixLabRunRepository runs, FakeTimeProvider time) = BuildSut();
            await SeedRunsAsync(runs, time, 3);

            IReadOnlyList<MixLabRunIndexEntry> page = await sut.ListAsync(take: 0, skip: 0, CancellationToken.None);

            page.Should().HaveCount(1);
        }

        [Test]
        public async Task GetAsync_unknown_run_returns_null()
        {
            (MixLabRunQueryUseCase sut, _, _) = BuildSut();

            MixLabRun? run = await sut.GetAsync("r_unknown", CancellationToken.None);

            run.Should().BeNull();
        }

        [Test]
        public async Task GetAsync_known_run_returns_manifest()
        {
            (MixLabRunQueryUseCase sut, BlobMixLabRunRepository runs, _) = BuildSut();
            MixLabRun created = await runs.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);

            MixLabRun? run = await sut.GetAsync(created.RunId, CancellationToken.None);

            run!.RunId.Should().Be(created.RunId);
            run.Status.Should().Be(MixLabRunStatus.Queued);
        }

        private static (MixLabRunQueryUseCase Sut, BlobMixLabRunRepository Runs, FakeTimeProvider Time) BuildSut()
        {
            var gateway = new FakeMixLabBlobGateway();
            var time = new FakeTimeProvider();
            var runs = new BlobMixLabRunRepository(gateway, time, NullLogger<BlobMixLabRunRepository>.Instance);
            var sut = new MixLabRunQueryUseCase(runs);
            return (sut, runs, time);
        }

        private static async Task SeedRunsAsync(BlobMixLabRunRepository runs, FakeTimeProvider time, int count)
        {
            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            for (int i = 0; i < count; i++)
            {
                time.UtcNow = time.UtcNow.AddMinutes(1);
                await runs.CreateQueuedAsync(MakeFlags(), $"u_{i}", CancellationToken.None);
            }
        }

        private static MixLabRunFlags MakeFlags()
        {
            return new MixLabRunFlags
            {
                Genre = "techno",
                Mode = "all",
                Risk = "high",
                Directions = "mixed",
            };
        }
    }
}
