using System.Net;
using System.Text.Json;
using Messenger.Application.Common;

namespace Messenger.Api.Services;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task Invoke(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AppException exception)
        {
            context.Response.StatusCode = (int)exception.StatusCode;
            context.Response.ContentType = "application/json";

            var payload = JsonSerializer.Serialize(new { error = exception.Message });
            await context.Response.WriteAsync(payload);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled server error");

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var payload = JsonSerializer.Serialize(new { error = "An unexpected server error occurred." });
            await context.Response.WriteAsync(payload);
        }
    }
}
