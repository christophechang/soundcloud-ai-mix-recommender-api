using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Changsta.Ai.Infrastructure.Services.Ai.Configuration
{
    /// <summary>
    /// Startup validator for <see cref="OpenAiOptions"/>. Surfaces a missing ApiKey / Model at host
    /// start so a misconfigured deploy fails immediately rather than first-request.
    /// </summary>
    public sealed class OpenAiOptionsValidator : IValidateOptions<OpenAiOptions>
    {
        public ValidateOptionsResult Validate(string? name, OpenAiOptions options)
        {
            var failures = new List<string>();

            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                failures.Add("OpenAI:ApiKey is not configured.");
            }

            if (string.IsNullOrWhiteSpace(options.Model))
            {
                failures.Add("OpenAI:Model is not configured.");
            }

            if (options.TimeoutSeconds <= 0)
            {
                failures.Add("OpenAI:TimeoutSeconds must be greater than zero.");
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }
    }
}
