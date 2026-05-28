using Changsta.Ai.Infrastructure.Services.Ai.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Configuration
{
    [TestFixture]
    public sealed class OpenAiOptionsValidatorTests
    {
        [Test]
        public void Validate_passes_when_both_fields_are_set()
        {
            var sut = new OpenAiOptionsValidator();

            ValidateOptionsResult result = sut.Validate(name: null, new OpenAiOptions
            {
                ApiKey = "sk-test",
                Model = "gpt-4.1-mini",
            });

            result.Succeeded.Should().BeTrue();
        }

        [Test]
        public void Validate_fails_when_ApiKey_is_empty()
        {
            var sut = new OpenAiOptionsValidator();

            ValidateOptionsResult result = sut.Validate(name: null, new OpenAiOptions
            {
                ApiKey = string.Empty,
                Model = "gpt-4.1-mini",
            });

            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain("OpenAI:ApiKey");
        }

        [Test]
        public void Validate_fails_when_Model_is_whitespace()
        {
            var sut = new OpenAiOptionsValidator();

            ValidateOptionsResult result = sut.Validate(name: null, new OpenAiOptions
            {
                ApiKey = "sk-test",
                Model = "   ",
            });

            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain("OpenAI:Model");
        }

        [Test]
        public void Validate_reports_both_failures_when_both_fields_missing()
        {
            var sut = new OpenAiOptionsValidator();

            ValidateOptionsResult result = sut.Validate(name: null, new OpenAiOptions
            {
                ApiKey = string.Empty,
                Model = string.Empty,
            });

            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain("OpenAI:ApiKey");
            result.FailureMessage.Should().Contain("OpenAI:Model");
        }
    }
}
