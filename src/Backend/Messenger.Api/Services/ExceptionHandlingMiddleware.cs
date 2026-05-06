// Перехват исключений API: единый JSON-ответ об ошибках и запись в лог.
using System.Net;
using System.Text.Json;
using Messenger.Application.Common;

namespace Messenger.Api.Services;

public sealed class ExceptionHandlingMiddleware
{
    private static readonly string LogFilePath = ResolveLogFilePath();

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
        }
        catch
        {
        }
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

    private static string ResolveLogFilePath()
    {
        const string fileName = "backend-app.log";

        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("BITCANARY_LOG_DIR"),
            @"E:\Programming\CsharpProj\BitCanary\storage\logs",
            Path.Combine(AppContext.BaseDirectory, "storage", "logs"),
            Path.Combine(Path.GetTempPath(), "BitCanary", "logs")
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(candidate);
                return Path.Combine(candidate, fileName);
            }
            catch
            {
            }
        }

        return fileName;
    }
}
