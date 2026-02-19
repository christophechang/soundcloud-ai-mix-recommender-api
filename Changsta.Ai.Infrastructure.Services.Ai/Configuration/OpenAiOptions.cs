namespace Changsta.Ai.Infrastructure.Services.Ai.Configuration
{
    public sealed class OpenAiOptions
    {
        required public string ApiKey { get; init; }

        required public string Model { get; init; }
    }
}