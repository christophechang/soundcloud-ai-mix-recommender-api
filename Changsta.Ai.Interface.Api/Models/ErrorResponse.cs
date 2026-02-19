namespace Changsta.Ai.Interface.Api.Models
{
    public sealed class ErrorResponse
    {
        required public string Message { get; init; }

        public string? CorrelationId { get; init; }
    }
}
