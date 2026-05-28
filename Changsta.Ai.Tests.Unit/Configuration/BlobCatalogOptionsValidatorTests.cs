using Changsta.Ai.Infrastructure.Services.Azure.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Configuration
{
    [TestFixture]
    public sealed class BlobCatalogOptionsValidatorTests
    {
        [Test]
        public void Validate_passes_with_service_endpoint()
        {
            var sut = new BlobCatalogOptionsValidator();

            ValidateOptionsResult result = sut.Validate(name: null, new BlobCatalogOptions
            {
                ServiceEndpoint = "https://test.blob.core.windows.net",
                ContainerName = "mix-catalog",
                BlobName = "catalog.json",
            });

            result.Succeeded.Should().BeTrue();
        }

        [Test]
        public void Validate_passes_with_connection_string()
        {
            var sut = new BlobCatalogOptionsValidator();

            ValidateOptionsResult result = sut.Validate(name: null, new BlobCatalogOptions
            {
                ConnectionString = "UseDevelopmentStorage=true",
                ContainerName = "mix-catalog",
                BlobName = "catalog.json",
            });

            result.Succeeded.Should().BeTrue();
        }

        [Test]
        public void Validate_fails_when_neither_ConnectionString_nor_ServiceEndpoint_is_set()
        {
            var sut = new BlobCatalogOptionsValidator();

            ValidateOptionsResult result = sut.Validate(name: null, new BlobCatalogOptions
            {
                ContainerName = "mix-catalog",
                BlobName = "catalog.json",
            });

            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain("ConnectionString");
            result.FailureMessage.Should().Contain("ServiceEndpoint");
        }

        [Test]
        public void Validate_fails_when_ContainerName_is_empty()
        {
            var sut = new BlobCatalogOptionsValidator();

            ValidateOptionsResult result = sut.Validate(name: null, new BlobCatalogOptions
            {
                ConnectionString = "UseDevelopmentStorage=true",
                ContainerName = string.Empty,
                BlobName = "catalog.json",
            });

            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain("ContainerName");
        }

        [Test]
        public void Validate_fails_when_BlobName_is_whitespace()
        {
            var sut = new BlobCatalogOptionsValidator();

            ValidateOptionsResult result = sut.Validate(name: null, new BlobCatalogOptions
            {
                ConnectionString = "UseDevelopmentStorage=true",
                ContainerName = "mix-catalog",
                BlobName = "   ",
            });

            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain("BlobName");
        }
    }
}
