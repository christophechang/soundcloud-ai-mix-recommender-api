using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.BusinessProcesses.MixLab;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;
using Changsta.Ai.Infrastructure.Services.Azure.MixLab;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    [TestFixture]
    public sealed class EnqueueMixLabRunUseCaseTests
    {
        private const string ValidFlags = "{\"genre\":\"techno\",\"mode\":\"all\",\"risk\":\"high\",\"directions\":\"mixed\"}";

        [Test]
        public async Task EnqueueAsync_valid_flags_and_concrete_upload_creates_queued_run()
        {
            (EnqueueMixLabRunUseCase sut, BlobMixLabRunRepository runs, BlobMixLabUploadRepository uploads, _) = BuildSut();
            string uploadId = await SeedUploadAsync(uploads);

            EnqueueMixLabRunResult result = await sut.EnqueueAsync(Json(ValidFlags), uploadId, CancellationToken.None);

            result.Outcome.Should().Be(EnqueueMixLabRunResult.EnqueueOutcome.Created);
            result.RunId.Should().NotBeNullOrEmpty();

            MixLabRun? run = await runs.GetAsync(result.RunId!, CancellationToken.None);
            run!.Status.Should().Be(MixLabRunStatus.Queued);
            run.UploadId.Should().Be(uploadId);
            run.Flags.Genre.Should().Be("techno");
        }

        [Test]
        public async Task EnqueueAsync_full_flag_set_is_accepted_and_persisted()
        {
            (EnqueueMixLabRunUseCase sut, BlobMixLabRunRepository runs, BlobMixLabUploadRepository uploads, _) = BuildSut();
            string uploadId = await SeedUploadAsync(uploads);
            const string flags = "{\"genre\":\"house\",\"mode\":\"played\",\"risk\":\"low\",\"directions\":\"only\","
                + "\"intent\":\"warmup\",\"mixLength\":90,\"resequence\":true,\"deep\":false,\"stage1Seed\":42}";

            EnqueueMixLabRunResult result = await sut.EnqueueAsync(Json(flags), uploadId, CancellationToken.None);

            result.Outcome.Should().Be(EnqueueMixLabRunResult.EnqueueOutcome.Created);
            MixLabRun? run = await runs.GetAsync(result.RunId!, CancellationToken.None);
            run!.Flags.MixLength.Should().Be(90);
            run.Flags.Resequence.Should().BeTrue();
            run.Flags.Stage1Seed.Should().Be(42);
            run.Flags.Intent.Should().Be("warmup");
        }

        [Test]
        public async Task EnqueueAsync_latest_resolves_to_newest_upload()
        {
            (EnqueueMixLabRunUseCase sut, BlobMixLabRunRepository runs, BlobMixLabUploadRepository uploads, FakeTimeProvider time) = BuildSut();

            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            await SeedUploadAsync(uploads);
            time.UtcNow = time.UtcNow.AddMinutes(1);
            string newest = await SeedUploadAsync(uploads);

            EnqueueMixLabRunResult result = await sut.EnqueueAsync(Json(ValidFlags), "latest", CancellationToken.None);

            MixLabRun? run = await runs.GetAsync(result.RunId!, CancellationToken.None);
            run!.UploadId.Should().Be(newest);
        }

        [Test]
        public async Task EnqueueAsync_latest_is_frozen_at_enqueue_time()
        {
            (EnqueueMixLabRunUseCase sut, BlobMixLabRunRepository runs, BlobMixLabUploadRepository uploads, FakeTimeProvider time) = BuildSut();

            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            string first = await SeedUploadAsync(uploads);

            EnqueueMixLabRunResult result = await sut.EnqueueAsync(Json(ValidFlags), "latest", CancellationToken.None);

            // A later upload must not change the already-queued run's frozen upload id.
            time.UtcNow = time.UtcNow.AddMinutes(5);
            string second = await SeedUploadAsync(uploads);

            MixLabRun? run = await runs.GetAsync(result.RunId!, CancellationToken.None);
            run!.UploadId.Should().Be(first);
            run.UploadId.Should().NotBe(second);
        }

        [Test]
        public async Task EnqueueAsync_latest_with_no_uploads_is_rejected()
        {
            (EnqueueMixLabRunUseCase sut, _, _, _) = BuildSut();

            EnqueueMixLabRunResult result = await sut.EnqueueAsync(Json(ValidFlags), "latest", CancellationToken.None);

            result.Outcome.Should().Be(EnqueueMixLabRunResult.EnqueueOutcome.NoUploadsAvailable);
        }

        [Test]
        public async Task EnqueueAsync_unknown_concrete_upload_is_not_found()
        {
            (EnqueueMixLabRunUseCase sut, _, _, _) = BuildSut();

            EnqueueMixLabRunResult result = await sut.EnqueueAsync(Json(ValidFlags), "u_does_not_exist", CancellationToken.None);

            result.Outcome.Should().Be(EnqueueMixLabRunResult.EnqueueOutcome.UnknownUpload);
        }

        [Test]
        public async Task EnqueueAsync_missing_upload_id_is_invalid()
        {
            (EnqueueMixLabRunUseCase sut, _, _, _) = BuildSut();

            EnqueueMixLabRunResult result = await sut.EnqueueAsync(Json(ValidFlags), null, CancellationToken.None);

            result.Outcome.Should().Be(EnqueueMixLabRunResult.EnqueueOutcome.InvalidRequest);
            result.ErrorMessage.Should().Contain("uploadId");
        }

        [Test]
        public async Task EnqueueAsync_unknown_flag_key_is_rejected_and_names_the_key()
        {
            (EnqueueMixLabRunUseCase sut, _, BlobMixLabUploadRepository uploads, _) = BuildSut();
            string uploadId = await SeedUploadAsync(uploads);
            const string flags = "{\"genre\":\"techno\",\"mode\":\"all\",\"risk\":\"high\",\"directions\":\"mixed\",\"bogus\":1}";

            EnqueueMixLabRunResult result = await sut.EnqueueAsync(Json(flags), uploadId, CancellationToken.None);

            result.Outcome.Should().Be(EnqueueMixLabRunResult.EnqueueOutcome.InvalidRequest);
            result.ErrorMessage.Should().Contain("bogus");
        }

        [TestCase("{\"mode\":\"all\",\"risk\":\"high\",\"directions\":\"mixed\"}", "genre", TestName = "missing genre")]
        [TestCase("{\"genre\":\"\",\"mode\":\"all\",\"risk\":\"high\",\"directions\":\"mixed\"}", "genre", TestName = "empty genre")]
        [TestCase("{\"genre\":5,\"mode\":\"all\",\"risk\":\"high\",\"directions\":\"mixed\"}", "genre", TestName = "non-string genre")]
        [TestCase("{\"genre\":\"t\",\"mode\":\"nope\",\"risk\":\"high\",\"directions\":\"mixed\"}", "mode", TestName = "bad mode")]
        [TestCase("{\"genre\":\"t\",\"mode\":\"all\",\"risk\":\"nope\",\"directions\":\"mixed\"}", "risk", TestName = "bad risk")]
        [TestCase("{\"genre\":\"t\",\"mode\":\"all\",\"risk\":\"high\",\"directions\":\"nope\"}", "directions", TestName = "bad directions")]
        [TestCase("{\"genre\":\"t\",\"mode\":\"all\",\"risk\":\"high\",\"directions\":\"mixed\",\"mixLength\":19}", "mixLength", TestName = "mixLength below range")]
        [TestCase("{\"genre\":\"t\",\"mode\":\"all\",\"risk\":\"high\",\"directions\":\"mixed\",\"mixLength\":241}", "mixLength", TestName = "mixLength above range")]
        [TestCase("{\"genre\":\"t\",\"mode\":\"all\",\"risk\":\"high\",\"directions\":\"mixed\",\"mixLength\":\"x\"}", "mixLength", TestName = "mixLength not int")]
        [TestCase("{\"genre\":\"t\",\"mode\":\"all\",\"risk\":\"high\",\"directions\":\"mixed\",\"stage1Seed\":1.5}", "stage1Seed", TestName = "stage1Seed not int")]
        [TestCase("{\"genre\":\"t\",\"mode\":\"all\",\"risk\":\"high\",\"directions\":\"mixed\",\"resequence\":\"yes\"}", "resequence", TestName = "resequence not bool")]
        public async Task EnqueueAsync_bad_flag_value_is_rejected_naming_the_field(string flagsJson, string expectedKey)
        {
            (EnqueueMixLabRunUseCase sut, _, BlobMixLabUploadRepository uploads, _) = BuildSut();
            string uploadId = await SeedUploadAsync(uploads);

            EnqueueMixLabRunResult result = await sut.EnqueueAsync(Json(flagsJson), uploadId, CancellationToken.None);

            result.Outcome.Should().Be(EnqueueMixLabRunResult.EnqueueOutcome.InvalidRequest);
            result.ErrorMessage.Should().Contain(expectedKey);
        }

        [Test]
        public async Task EnqueueAsync_intent_over_500_chars_is_rejected()
        {
            (EnqueueMixLabRunUseCase sut, _, BlobMixLabUploadRepository uploads, _) = BuildSut();
            string uploadId = await SeedUploadAsync(uploads);
            string intent = new string('x', 501);
            string flags = "{\"genre\":\"t\",\"mode\":\"all\",\"risk\":\"high\",\"directions\":\"mixed\",\"intent\":\"" + intent + "\"}";

            EnqueueMixLabRunResult result = await sut.EnqueueAsync(Json(flags), uploadId, CancellationToken.None);

            result.Outcome.Should().Be(EnqueueMixLabRunResult.EnqueueOutcome.InvalidRequest);
            result.ErrorMessage.Should().Contain("intent");
        }

        [TestCase(20)]
        [TestCase(240)]
        public async Task EnqueueAsync_mixLength_boundaries_are_accepted(int mixLength)
        {
            (EnqueueMixLabRunUseCase sut, BlobMixLabRunRepository runs, BlobMixLabUploadRepository uploads, _) = BuildSut();
            string uploadId = await SeedUploadAsync(uploads);
            string flags = "{\"genre\":\"t\",\"mode\":\"all\",\"risk\":\"high\",\"directions\":\"mixed\",\"mixLength\":" + mixLength + "}";

            EnqueueMixLabRunResult result = await sut.EnqueueAsync(Json(flags), uploadId, CancellationToken.None);

            result.Outcome.Should().Be(EnqueueMixLabRunResult.EnqueueOutcome.Created);
            MixLabRun? run = await runs.GetAsync(result.RunId!, CancellationToken.None);
            run!.Flags.MixLength.Should().Be(mixLength);
        }

        [Test]
        public async Task EnqueueAsync_non_object_flags_is_invalid()
        {
            (EnqueueMixLabRunUseCase sut, _, BlobMixLabUploadRepository uploads, _) = BuildSut();
            string uploadId = await SeedUploadAsync(uploads);

            EnqueueMixLabRunResult result = await sut.EnqueueAsync(Json("\"not-an-object\""), uploadId, CancellationToken.None);

            result.Outcome.Should().Be(EnqueueMixLabRunResult.EnqueueOutcome.InvalidRequest);
        }

        private static (EnqueueMixLabRunUseCase Sut, BlobMixLabRunRepository Runs, BlobMixLabUploadRepository Uploads, FakeTimeProvider Time) BuildSut()
        {
            var gateway = new FakeMixLabBlobGateway();
            var time = new FakeTimeProvider();
            var runs = new BlobMixLabRunRepository(gateway, time, NullLogger<BlobMixLabRunRepository>.Instance);
            var uploads = new BlobMixLabUploadRepository(gateway, time, NullLogger<BlobMixLabUploadRepository>.Instance);
            var sut = new EnqueueMixLabRunUseCase(runs, uploads, NullLogger<EnqueueMixLabRunUseCase>.Instance);
            return (sut, runs, uploads, time);
        }

        private static async Task<string> SeedUploadAsync(BlobMixLabUploadRepository uploads)
        {
            using var content = new MemoryStream(new byte[] { 1, 2, 3 });
            MixLabUpload upload = await uploads.SaveAsync(content, 3, null, CancellationToken.None);
            return upload.UploadId;
        }

        private static JsonElement Json(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }
}
