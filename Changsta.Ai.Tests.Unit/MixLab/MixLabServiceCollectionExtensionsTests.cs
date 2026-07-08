using System.Collections.Generic;
using Changsta.Ai.Core.BusinessProcesses.MixLab;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Interface.Api.MixLab;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    /// <summary>
    /// DI resolution smoke test for <see cref="MixLabServiceCollectionExtensions.AddMixLabServices"/>.
    /// Scope: this configures "Azure:MixLab:ConnectionString" as
    /// <c>UseDevelopmentStorage=true</c> — the same well-formed local-dev connection string used by
    /// <c>BlobContainerClientFactoryTests</c> — so the real <c>AddMixLabAzureServices</c> (A1) runs
    /// unmodified and every singleton it registers (blob gateway, repositories, the
    /// DefaultAzureCredential-backed TokenCredential) can be constructed with no network I/O: the
    /// Azure SDK's BlobContainerClient parses the connection string into a client without ever
    /// calling out, and nothing here invokes a use case method, so no actual blob call happens. This
    /// deliberately does not fake or bypass A1 — it proves the full composition graph the ticket
    /// wires together resolves end to end. See issue #132.
    /// </summary>
    [TestFixture]
    public sealed class MixLabServiceCollectionExtensionsTests
    {
        [Test]
        public void AddMixLabServices_resolves_all_upload_and_run_queue_use_cases()
        {
            using ServiceProvider provider = BuildProvider();

            provider.GetRequiredService<IUploadCollectionUseCase>().Should().NotBeNull();
            provider.GetRequiredService<IGetUploadsUseCase>().Should().NotBeNull();
            provider.GetRequiredService<IOpenUploadUseCase>().Should().NotBeNull();
            provider.GetRequiredService<IEnqueueMixLabRunUseCase>().Should().NotBeNull();
            provider.GetRequiredService<IClaimMixLabRunUseCase>().Should().NotBeNull();
            provider.GetRequiredService<ICompleteMixLabRunUseCase>().Should().NotBeNull();
            provider.GetRequiredService<IFailMixLabRunUseCase>().Should().NotBeNull();
            provider.GetRequiredService<IMixLabRunQueryUseCase>().Should().NotBeNull();
            provider.GetRequiredService<IOpenMixLabRunArtifactUseCase>().Should().NotBeNull();
        }

        [Test]
        public void AddMixLabServices_binds_MixLabOptions_from_configuration()
        {
            using ServiceProvider provider = BuildProvider();

            MixLabOptions options = provider.GetRequiredService<MixLabOptions>();

            options.ClaimLeaseMinutes.Should().Be(45);
        }

        [Test]
        public void AddMixLabServices_defaults_MixLabOptions_when_section_absent()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Azure:MixLab:ConnectionString"] = "UseDevelopmentStorage=true",
                    ["Azure:MixLab:ContainerName"] = "mixlab",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMixLabServices(configuration);
            using ServiceProvider provider = services.BuildServiceProvider();

            MixLabOptions options = provider.GetRequiredService<MixLabOptions>();

            options.ClaimLeaseMinutes.Should().Be(45);
        }

        private static ServiceProvider BuildProvider()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Azure:MixLab:ConnectionString"] = "UseDevelopmentStorage=true",
                    ["Azure:MixLab:ContainerName"] = "mixlab",
                    ["MixLab:ClaimLeaseMinutes"] = "45",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMixLabServices(configuration);
            return services.BuildServiceProvider();
        }
    }
}
