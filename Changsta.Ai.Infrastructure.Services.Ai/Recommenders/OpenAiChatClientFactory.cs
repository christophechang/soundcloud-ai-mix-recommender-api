using System;
using System.ClientModel;
using Changsta.Ai.Infrastructure.Services.Ai.Configuration;
using OpenAI;
using OpenAI.Chat;

namespace Changsta.Ai.Infrastructure.Services.Ai.Recommenders
{
    /// <summary>
    /// Builds <see cref="ChatClient"/> instances with a bounded per-request network timeout so a
    /// slow or degraded OpenAI endpoint cannot stall a request indefinitely (and exhaust threads).
    /// </summary>
    internal static class OpenAiChatClientFactory
    {
        public static ChatClient Create(OpenAiOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            var clientOptions = new OpenAIClientOptions
            {
                NetworkTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
            };

            return new ChatClient(options.Model, new ApiKeyCredential(options.ApiKey), clientOptions);
        }
    }
}
