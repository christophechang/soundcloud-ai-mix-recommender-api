using System.Text.Json;
using Changsta.Ai.Interface.Api.ViewModels;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Api
{
    [TestFixture]
    public sealed class MixStatsVmSerializationTests
    {
        [Test]
        public void MixStatsVm_writes_null_counts_as_null()
        {
            // Mirrors Program.cs: MVC's default (Web) serializer options plus the camelCase policy.
            JsonSerializerOptions options = new JsonOptions().JsonSerializerOptions;
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;

            var vm = new MixStatsVm { Id = "tag:soundcloud,2010:tracks/1", Plays = 12, Likes = null };

            string json = JsonSerializer.Serialize(vm, options);

            json.Should().Be("{\"id\":\"tag:soundcloud,2010:tracks/1\",\"plays\":12,\"likes\":null}");
        }
    }
}
