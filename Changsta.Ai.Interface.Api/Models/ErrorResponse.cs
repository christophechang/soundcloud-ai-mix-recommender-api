namespace Changsta.Ai.Interface.Api.Models
{
    public sealed class ErrorResponse
    {
        required public string Error { get; init; }

        public string? CorrelationId { get; init; }
    }
}
