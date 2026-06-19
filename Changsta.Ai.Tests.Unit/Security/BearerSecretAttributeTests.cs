using System.Collections.Generic;
using Changsta.Ai.Interface.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Security
{
    [TestFixture]
    public sealed class BearerSecretAttributeTests
    {
        [Test]
        public void Allows_when_secret_set_and_token_matches()
        {
            AuthorizationFilterContext context = BuildContext(
                secret: "s3cr3t", authHeader: "Bearer s3cr3t", environmentName: "Production");

            new BearerSecretAttribute("Catalog:FlushSecret").OnAuthorization(context);

            Assert.That(context.Result, Is.Null);
        }

        [Test]
        public void Rejects_when_secret_set_and_header_missing()
        {
            AuthorizationFilterContext context = BuildContext(
                secret: "s3cr3t", authHeader: null, environmentName: "Production");

            new BearerSecretAttribute("Catalog:FlushSecret").OnAuthorization(context);

            Assert.That(context.Result, Is.InstanceOf<UnauthorizedObjectResult>());
        }

        [Test]
        public void Rejects_when_secret_set_and_token_wrong()
        {
            AuthorizationFilterContext context = BuildContext(
                secret: "s3cr3t", authHeader: "Bearer nope", environmentName: "Production");

            new BearerSecretAttribute("Catalog:FlushSecret").OnAuthorization(context);

            Assert.That(context.Result, Is.InstanceOf<UnauthorizedObjectResult>());
        }

        [Test]
        public void Fails_closed_when_secret_unset_in_non_development()
        {
            AuthorizationFilterContext context = BuildContext(
                secret: null, authHeader: null, environmentName: "Production");

            new BearerSecretAttribute("Catalog:FlushSecret").OnAuthorization(context);

            Assert.That(context.Result, Is.InstanceOf<UnauthorizedObjectResult>());
        }

        [Test]
        public void Allows_when_secret_unset_in_development()
        {
            AuthorizationFilterContext context = BuildContext(
                secret: null, authHeader: null, environmentName: "Development");

            new BearerSecretAttribute("Catalog:FlushSecret").OnAuthorization(context);

            Assert.That(context.Result, Is.Null);
        }

        private static AuthorizationFilterContext BuildContext(string? secret, string? authHeader, string environmentName)
        {
            var configData = new List<KeyValuePair<string, string?>>();
            if (secret is not null)
            {
                configData.Add(new KeyValuePair<string, string?>("Catalog:FlushSecret", secret));
            }

            IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(environmentName));

            var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
            if (authHeader is not null)
            {
                httpContext.Request.Headers.Authorization = authHeader;
            }

            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
        }

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
