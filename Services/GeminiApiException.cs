using System.Net;

namespace LuKnight.Services;

public sealed class GeminiApiException(HttpStatusCode statusCode, string model, string message)
    : InvalidOperationException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Model { get; } = model;
    public bool AllowsModelFallback => StatusCode is HttpStatusCode.TooManyRequests
        or HttpStatusCode.ServiceUnavailable or HttpStatusCode.NotFound;
}
