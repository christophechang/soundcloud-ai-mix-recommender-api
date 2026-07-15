using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;
using Changsta.Ai.Interface.Api.Controllers;
using Changsta.Ai.Interface.Api.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    [TestFixture]
    public sealed class MixLabUploadsControllerTests
    {
        [Test]
        public async Task UploadAsync_returns_201_with_uploadId_and_sizeBytes()
        {
            var upload = new MixLabUpload { UploadId = "u_1", UploadedAt = DateTimeOffset.UtcNow, SizeBytes = 42 };
            var sut = BuildSut(uploadUseCase: new StubUploadCollectionUseCase(upload));

            IActionResult result = await InvokeUploadAsync(sut, label: null, contentEncoding: null);

            var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
            objectResult.StatusCode.Should().Be(201);
        }

        [Test]
        public async Task UploadAsync_no_content_encoding_header_passes_false_flag_to_use_case()
        {
            var spy = new SpyUploadCollectionUseCase();
            var sut = BuildSut(uploadUseCase: spy);

            await InvokeUploadAsync(sut, label: null, contentEncoding: null);

            spy.ContentEncodingSaysGzipReceived.Should().BeFalse();
        }

        [Test]
        public async Task UploadAsync_content_encoding_gzip_header_passes_true_flag_to_use_case()
        {
            var spy = new SpyUploadCollectionUseCase();
            var sut = BuildSut(uploadUseCase: spy);

            await InvokeUploadAsync(sut, label: null, contentEncoding: "gzip");

            spy.ContentEncodingSaysGzipReceived.Should().BeTrue();
        }

        [Test]
        public async Task UploadAsync_passes_label_query_param_to_use_case()
        {
            var spy = new SpyUploadCollectionUseCase();
            var sut = BuildSut(uploadUseCase: spy);

            await InvokeUploadAsync(sut, label: "my-label", contentEncoding: null);

            spy.LabelReceived.Should().Be("my-label");
        }

        [Test]
        public async Task GetUploadsAsync_returns_ok_with_index()
        {
            var uploads = new[] { new MixLabUpload { UploadId = "u_1", UploadedAt = DateTimeOffset.UtcNow, SizeBytes = 1 } };
            var sut = BuildSut(getUploadsUseCase: new StubGetUploadsUseCase(uploads));

            IActionResult result = await sut.GetUploadsAsync(CancellationToken.None);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().BeSameAs(uploads);
        }

        [Test]
        public async Task GetUploadAsync_returns_404_when_not_found()
        {
            var sut = BuildSut(openUploadUseCase: new StubOpenUploadUseCase(null));

            IActionResult result = await sut.GetUploadAsync("unknown", CancellationToken.None);

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Test]
        public async Task GetUploadAsync_returns_gzip_file_result_when_found()
        {
            var content = new MixLabUploadContent("u_1", new MemoryStream(Encoding.UTF8.GetBytes("gzip-bytes")));
            var sut = BuildSut(openUploadUseCase: new StubOpenUploadUseCase(content));

            IActionResult result = await sut.GetUploadAsync("u_1", CancellationToken.None);

            var fileResult = result.Should().BeOfType<FileStreamResult>().Subject;
            fileResult.ContentType.Should().Be("application/gzip");
        }

        [Test]
        public async Task GetUploadPlaylistsAsync_returns_ok_with_paths_when_found()
        {
            var stub = new StubListUploadPlaylistsUseCase(new ListUploadPlaylistsResult
            {
                Outcome = ListUploadPlaylistsResult.ListOutcome.Found,
                Playlists = new[] { "Crates/Eclectic" },
            });
            var sut = BuildSut(listPlaylistsUseCase: stub);

            IActionResult result = await sut.GetUploadPlaylistsAsync("u_1", CancellationToken.None);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().BeEquivalentTo(new[] { "Crates/Eclectic" });
        }

        [Test]
        public async Task GetUploadPlaylistsAsync_returns_404_when_upload_not_found()
        {
            var stub = new StubListUploadPlaylistsUseCase(new ListUploadPlaylistsResult
            {
                Outcome = ListUploadPlaylistsResult.ListOutcome.UploadNotFound,
            });
            var sut = BuildSut(listPlaylistsUseCase: stub);

            IActionResult result = await sut.GetUploadPlaylistsAsync("missing", CancellationToken.None);

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Test]
        public async Task GetUploadPlaylistsAsync_returns_400_when_parse_failed()
        {
            var stub = new StubListUploadPlaylistsUseCase(new ListUploadPlaylistsResult
            {
                Outcome = ListUploadPlaylistsResult.ListOutcome.ParseFailed,
                ErrorMessage = "bad xml",
            });
            var sut = BuildSut(listPlaylistsUseCase: stub);

            IActionResult result = await sut.GetUploadPlaylistsAsync("u_1", CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Test]
        public void Route_is_api_mixlab()
        {
            var routeAttribute = typeof(MixLabUploadsController)
                .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
                .Cast<RouteAttribute>()
                .Single();

            routeAttribute.Template.Should().Be("api/mixlab");
        }

        [Test]
        public void Controller_requires_BearerSecret_for_MixLab_ApiSecret()
        {
            var bearerSecretAttribute = typeof(MixLabUploadsController)
                .GetCustomAttributes(typeof(BearerSecretAttribute), inherit: false)
                .Cast<BearerSecretAttribute>()
                .Single();

            GetConfigurationKey(bearerSecretAttribute).Should().Be("MixLab:ApiSecret");
        }

        [Test]
        public void UploadAsync_enforces_64_megabyte_request_size_limit()
        {
            var requestSizeLimitAttribute = typeof(MixLabUploadsController)
                .GetMethod(nameof(MixLabUploadsController.UploadAsync)) !
                .GetCustomAttributes(typeof(RequestSizeLimitAttribute), inherit: false)
                .Cast<RequestSizeLimitAttribute>()
                .Single();

            GetRequestSizeLimitBytes(requestSizeLimitAttribute).Should().Be(64 * 1024 * 1024);
        }

        private static long GetRequestSizeLimitBytes(RequestSizeLimitAttribute attribute)
        {
            return (long)typeof(RequestSizeLimitAttribute)
                .GetField("_bytes", BindingFlags.NonPublic | BindingFlags.Instance) !
                .GetValue(attribute) !;
        }

        private static string GetConfigurationKey(BearerSecretAttribute attribute)
        {
            return (string)typeof(BearerSecretAttribute)
                .GetField("_configurationKey", BindingFlags.NonPublic | BindingFlags.Instance) !
                .GetValue(attribute) !;
        }

        private static async Task<IActionResult> InvokeUploadAsync(MixLabUploadsController sut, string? label, string? contentEncoding)
        {
            var httpContext = new DefaultHttpContext
            {
                Request = { Body = new MemoryStream(Encoding.UTF8.GetBytes("body")) },
            };

            if (contentEncoding is not null)
            {
                httpContext.Request.Headers["Content-Encoding"] = contentEncoding;
            }

            sut.ControllerContext = new ControllerContext { HttpContext = httpContext };

            return await sut.UploadAsync(label, CancellationToken.None);
        }

        private static MixLabUploadsController BuildSut(
            IUploadCollectionUseCase? uploadUseCase = null,
            IGetUploadsUseCase? getUploadsUseCase = null,
            IOpenUploadUseCase? openUploadUseCase = null,
            IListUploadPlaylistsUseCase? listPlaylistsUseCase = null)
        {
            var defaultUpload = new MixLabUpload { UploadId = "u_1", UploadedAt = DateTimeOffset.UtcNow, SizeBytes = 0 };

            var sut = new MixLabUploadsController(
                uploadUseCase ?? new StubUploadCollectionUseCase(defaultUpload),
                getUploadsUseCase ?? new StubGetUploadsUseCase(Array.Empty<MixLabUpload>()),
                openUploadUseCase ?? new StubOpenUploadUseCase(null),
                listPlaylistsUseCase ?? new StubListUploadPlaylistsUseCase(new ListUploadPlaylistsResult { Outcome = ListUploadPlaylistsResult.ListOutcome.Found }));

            sut.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

            return sut;
        }

        private sealed class StubUploadCollectionUseCase : IUploadCollectionUseCase
        {
            private readonly MixLabUpload _upload;

            public StubUploadCollectionUseCase(MixLabUpload upload)
            {
                _upload = upload;
            }

            public Task<MixLabUpload> UploadAsync(Stream body, bool contentEncodingSaysGzip, string? label, CancellationToken cancellationToken) =>
                Task.FromResult(_upload);
        }

        private sealed class SpyUploadCollectionUseCase : IUploadCollectionUseCase
        {
            public bool ContentEncodingSaysGzipReceived { get; private set; }

            public string? LabelReceived { get; private set; }

            public Task<MixLabUpload> UploadAsync(Stream body, bool contentEncodingSaysGzip, string? label, CancellationToken cancellationToken)
            {
                ContentEncodingSaysGzipReceived = contentEncodingSaysGzip;
                LabelReceived = label;
                return Task.FromResult(new MixLabUpload { UploadId = "u_1", UploadedAt = DateTimeOffset.UtcNow, SizeBytes = 0, Label = label });
            }
        }

        private sealed class StubGetUploadsUseCase : IGetUploadsUseCase
        {
            private readonly IReadOnlyList<MixLabUpload> _uploads;

            public StubGetUploadsUseCase(IReadOnlyList<MixLabUpload> uploads)
            {
                _uploads = uploads;
            }

            public Task<IReadOnlyList<MixLabUpload>> GetUploadsAsync(CancellationToken cancellationToken) => Task.FromResult(_uploads);
        }

        private sealed class StubOpenUploadUseCase : IOpenUploadUseCase
        {
            private readonly MixLabUploadContent? _content;

            public StubOpenUploadUseCase(MixLabUploadContent? content)
            {
                _content = content;
            }

            public Task<MixLabUploadContent?> OpenAsync(string uploadId, CancellationToken cancellationToken) => Task.FromResult(_content);
        }

        private sealed class StubListUploadPlaylistsUseCase : IListUploadPlaylistsUseCase
        {
            private readonly ListUploadPlaylistsResult _result;

            public StubListUploadPlaylistsUseCase(ListUploadPlaylistsResult result)
            {
                _result = result;
            }

            public Task<ListUploadPlaylistsResult> ListAsync(string uploadId, CancellationToken cancellationToken) =>
                Task.FromResult(_result);
        }
    }
}
