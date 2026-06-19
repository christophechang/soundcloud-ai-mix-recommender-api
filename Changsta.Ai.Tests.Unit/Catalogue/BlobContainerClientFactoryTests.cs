using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Changsta.Ai.Infrastructure.Services.Azure.Catalogue;
using Changsta.Ai.Infrastructure.Services.Azure.Configuration;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Catalogue
{
    [TestFixture]
    public sealed class BlobContainerClientFactoryTests
    {
        [Test]
        public void Create_uses_service_endpoint_with_credential()
        {
            var options = new BlobCatalogOptions
            {
                ServiceEndpoint = "https://acct.blob.core.windows.net",
                ContainerName = "mix-catalog",
                BlobName = "catalog.json",
            };

            var client = BlobContainerClientFactory.Create(options, new FakeCredential());

            client.Name.Should().Be("mix-catalog");
            client.Uri.ToString().Should().Be("https://acct.blob.core.windows.net/mix-catalog");
        }

        [Test]
        public void Create_uses_connection_string_when_no_service_endpoint()
        {
            var options = new BlobCatalogOptions
            {
                ConnectionString = "UseDevelopmentStorage=true",
                ContainerName = "mix-catalog",
                BlobName = "catalog.json",
            };

            var client = BlobContainerClientFactory.Create(options, new FakeCredential());

            client.Name.Should().Be("mix-catalog");
        }

        [Test]
        public void Create_throws_on_null_arguments()
        {
            var options = new BlobCatalogOptions { ContainerName = "c", BlobName = "b" };

            ((Action)(() => BlobContainerClientFactory.Create(null!, new FakeCredential())))
                .Should().Throw<ArgumentNullException>();
            ((Action)(() => BlobContainerClientFactory.Create(options, null!)))
                .Should().Throw<ArgumentNullException>();
        }

        private sealed class FakeCredential : TokenCredential
        {
            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
                => new AccessToken("fake", DateTimeOffset.MaxValue);

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
                => new ValueTask<AccessToken>(new AccessToken("fake", DateTimeOffset.MaxValue));
        }
    }
}
