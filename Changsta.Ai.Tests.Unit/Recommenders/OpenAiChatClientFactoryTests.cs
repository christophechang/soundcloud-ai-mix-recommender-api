using System;
using Changsta.Ai.Infrastructure.Services.Ai.Configuration;
using Changsta.Ai.Infrastructure.Services.Ai.Recommenders;
using FluentAssertions;
using NUnit.Framework;
using OpenAI.Chat;

namespace Changsta.Ai.Tests.Unit.Recommenders
{
    [TestFixture]
    public sealed class OpenAiChatClientFactoryTests
    {
        [Test]
        public void Create_returns_a_chat_client_for_valid_options()
        {
            var options = new OpenAiOptions
            {
                ApiKey = "sk-test-key",
                Model = "gpt-4.1-mini",
                TimeoutSeconds = 30,
            };

            ChatClient client = OpenAiChatClientFactory.Create(options);

            client.Should().NotBeNull();
        }

        [Test]
        public void Create_throws_on_null_options()
        {
            Action act = () => OpenAiChatClientFactory.Create(null!);

            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void Create_throws_when_api_key_is_blank()
        {
            var options = new OpenAiOptions
            {
                ApiKey = string.Empty,
                Model = "gpt-4.1-mini",
                TimeoutSeconds = 30,
            };

            Action act = () => OpenAiChatClientFactory.Create(options);

            act.Should().Throw<ArgumentException>();
        }
    }
}
