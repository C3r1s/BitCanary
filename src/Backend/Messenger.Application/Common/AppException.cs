using System.Net;

namespace Messenger.Application.Common;

public sealed class AppException(string message, HttpStatusCode statusCode = HttpStatusCode.BadRequest) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
