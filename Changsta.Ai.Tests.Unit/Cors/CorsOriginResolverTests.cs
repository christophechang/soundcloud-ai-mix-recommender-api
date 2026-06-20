using System;
using System.Collections.Generic;
using Changsta.Ai.Interface.Api.Cors;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Cors
{
    [TestFixture]
    public sealed class CorsOriginResolverTests
    {
        [Test]
        public void Development_appends_localhost_origin()
        {
            string[] result = CorsOriginResolver.Resolve(
                Config("https://changsta.com"),
                Env("Development"));

            result.Should().Contain("https://changsta.com");
            result.Should().Contain("http://localhost:8080");
        }

        [Test]
        public void Production_returns_https_origins_without_localhost()
        {
            string[] result = CorsOriginResolver.Resolve(
                Config("https://changsta.com", "https://www.changsta.com"),
                Env("Production"));

            result.Should().BeEquivalentTo(new[] { "https://changsta.com", "https://www.changsta.com" });
            result.Should().NotContain(o => o.Contains("localhost"));
        }

        [Test]
        public void Production_rejects_non_https_origin()
        {
            Action act = () => CorsOriginResolver.Resolve(
                Config("https://changsta.com", "http://changsta.com"),
                Env("Production"));

            act.Should().Throw<InvalidOperationException>().WithMessage("*https*");
        }

        [Test]
        public void Production_rejects_localhost_origin()
        {
            Action act = () => CorsOriginResolver.Resolve(
                Config("https://localhost:8080"),
                Env("Production"));

            act.Should().Throw<InvalidOperationException>().WithMessage("*localhost*");
        }

        [Test]
        public void Production_rejects_empty_origin_list()
        {
            Action act = () => CorsOriginResolver.Resolve(Config(), Env("Production"));

            act.Should().Throw<InvalidOperationException>();
        }

        private static IConfiguration Config(params string[] origins)
        {
            var data = new List<KeyValuePair<string, string?>>();
            for (int i = 0; i < origins.Length; i++)
            {
                data.Add(new KeyValuePair<string, string?>($"Cors:AllowedOrigins:{i}", origins[i]));
            }

            return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        }

        private static IHostEnvironment Env(string environmentName) => new FakeHostEnvironment(environmentName);

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
