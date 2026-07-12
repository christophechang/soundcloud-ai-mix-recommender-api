using System;
using System.Collections.Generic;
using Changsta.Ai.Interface.Api.Cors;
using FluentAssertions;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    [TestFixture]
    public sealed class MixLabCorsTests
    {
        [Test]
        public void Resolver_includes_mixlab_origin()
        {
            var configuration = BuildConfiguration("https://changsta.com", "https://www.changsta.com", "https://mixlab.changsta.com");
            var environment = BuildEnvironment("Production");

            string[] origins = CorsOriginResolver.Resolve(configuration, environment);

            origins.Should().Contain("https://mixlab.changsta.com");
        }

        [Test]
        public void Resolver_preserves_mixlab_origin_in_development()
        {
            var configuration = BuildConfiguration("https://changsta.com", "https://mixlab.changsta.com");
            var environment = BuildEnvironment("Development");

            string[] origins = CorsOriginResolver.Resolve(configuration, environment);

            origins.Should().Contain("https://mixlab.changsta.com");
            origins.Should().Contain("http://localhost:8080");
        }

        [Test]
        public void ChangstaSite_policy_can_be_built_with_content_encoding_and_etag()
        {
            var corsOptions = BuildCorsOptions();

            CorsPolicy? policy = corsOptions.GetPolicy("ChangstaSite");
            policy.Should().NotBeNull();

            // Verify origins
            policy !.Origins.Should().Contain("https://mixlab.changsta.com");

            // Verify methods
            policy.Methods.Should().Contain("GET");
            policy.Methods.Should().Contain("POST");
            policy.Methods.Should().Contain("PUT");
            policy.Methods.Should().Contain("DELETE");

            // Verify headers
            policy.Headers.Should().Contain("Content-Type");
            policy.Headers.Should().Contain("Authorization");
            policy.Headers.Should().Contain("Content-Encoding");

            // Verify exposed headers for history sync ETag
            policy.ExposedHeaders.Should().Contain("ETag");
        }

        private static CorsOptions BuildCorsOptions()
        {
            var configuration = BuildConfiguration("https://changsta.com", "https://www.changsta.com", "https://mixlab.changsta.com");
            var environment = BuildEnvironment("Production");

            string[] allowedOrigins = CorsOriginResolver.Resolve(configuration, environment);

            var corsOptions = new CorsOptions();
            corsOptions.AddPolicy("ChangstaSite", policy =>
            {
                policy
                    .WithOrigins(allowedOrigins)
                    .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
                    .WithHeaders("Content-Type", "Authorization", "Content-Encoding")
                    .WithExposedHeaders("ETag")
                    .SetPreflightMaxAge(TimeSpan.FromHours(12));
            });

            return corsOptions;
        }

        private static IConfiguration BuildConfiguration(params string[] origins)
        {
            var data = new List<KeyValuePair<string, string?>>();
            for (int i = 0; i < origins.Length; i++)
            {
                data.Add(new KeyValuePair<string, string?>($"Cors:AllowedOrigins:{i}", origins[i]));
            }

            return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        }

        private static IHostEnvironment BuildEnvironment(string environmentName) => new FakeHostEnvironment(environmentName);

        private sealed class FakeHostEnvironment : IHostEnvironment
        {
            public FakeHostEnvironment(string environmentName) => EnvironmentName = environmentName;

            public string EnvironmentName { get; set; }

            public string ApplicationName { get; set; } = "Changsta.Ai.Tests.Unit";

            public string ContentRootPath { get; set; } = string.Empty;

            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
