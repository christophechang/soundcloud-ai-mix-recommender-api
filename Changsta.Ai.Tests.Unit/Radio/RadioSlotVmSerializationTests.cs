using System.Text.Json;
using Changsta.Ai.Interface.Api.ViewModels;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Radio
{
    /// <summary>
    /// The field name is a published contract — the site keys off it — so it is pinned here rather
    /// than left to the serializer's defaults. Program.cs sets CamelCase globally; these assert the
    /// shape that produces on the wire.
    /// </summary>
    [TestFixture]
    public sealed class RadioSlotVmSerializationTests
    {
        [Test]
        public void RelaxedRules_serialises_as_camel_case()
        {
            string json = Serialize(new[] { "Score threshold relaxed." });

            json.Should().Contain("\"relaxedRules\"");
            json.Should().NotContain("\"RelaxedRules\"");
        }

        [Test]
        public void RelaxedRules_serialises_as_an_empty_array_when_the_slot_is_clean()
        {
            string json = Serialize(System.Array.Empty<string>());

            json.Should().Contain("\"relaxedRules\":[]");
        }

        [Test]
        public void RelaxedRules_preserves_the_order_the_scheduler_dropped_them_in()
        {
            string json = Serialize(new[] { "first.", "second." });

            json.Should().Contain("\"relaxedRules\":[\"first.\",\"second.\"]");
        }

        private static string Serialize(string[] relaxedRules)
        {
            var vm = new RadioSlotVm
            {
                Hour = 9,
                Mix = new RadioMixVm
                {
                    Id = "m1",
                    Title = "A - m1",
                    Url = "https://sc.test/m1",
                    Genre = "breakbeat",
                    Energy = "mid",
                },
                IsCurrent = true,
                RelaxedRules = relaxedRules,
            };

            return JsonSerializer.Serialize(
                vm,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
    }
}
