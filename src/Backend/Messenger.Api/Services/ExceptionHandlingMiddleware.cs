using System.Net;
using System.Text.Json;
using Messenger.Application.Common;

namespace Messenger.Api.Services;

public sealed class ExceptionHandlingMiddleware
{
    private static readonly string LogFilePath = Path.Combine(
        AppContext.BaseDirectory, "storage", "app.log");

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
    }

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppException exception)
        {
            LogToFile("ERROR", "AppException", exception.Message, exception);

            context.Response.StatusCode = (int)exception.StatusCode;
            context.Response.ContentType = "application/json";

            var payload = JsonSerializer.Serialize(new { error = exception.Message });
            await context.Response.WriteAsync(payload);
        }
        catch (Exception exception)
        {
            LogToFile("ERROR", "Unhandled", exception.Message, exception);
            _logger.LogError(exception, "Unhandled server error");

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var payload = JsonSerializer.Serialize(new { error = "An unexpected server error occurred." });
            await context.Response.WriteAsync(payload);
        }
    }

    private void LogToFile(string level, string source, string message, Exception? ex = null)
    {
        try
        {
            var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {source}: {message}";
            if (ex != null)
            {
                logLine += $"\n{ex}";
            }
            File.AppendAllText(LogFilePath, logLine + "\n\n");
        }
        catch
        {
        }
    }
}
