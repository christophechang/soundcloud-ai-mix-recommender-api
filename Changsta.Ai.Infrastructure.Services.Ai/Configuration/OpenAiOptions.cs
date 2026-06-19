namespace Changsta.Ai.Infrastructure.Services.Ai.Configuration
{
    public sealed class OpenAiOptions
    {
        required public string ApiKey { get; init; }

        required public string Model { get; init; }

        // Per-request network timeout, in seconds, applied to OpenAI calls. Bounds how long a
        // single request can stall on a slow/degraded upstream before failing. Defaults to 30.
        public int TimeoutSeconds { get; init; } = 30;
    }
}