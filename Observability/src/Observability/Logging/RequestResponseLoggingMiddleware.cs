using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Observability.Logging;

public class RequestResponseLoggingMiddleware : IMiddleware
{
    private readonly ILogger<RequestResponseLoggingMiddleware> _logger;
    public RequestResponseLoggingMiddleware(ILogger<RequestResponseLoggingMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var request = context.Request;
        request.EnableBuffering();
        string requestBody = "";
        if (request.ContentLength is > 0 && request.Body.CanRead)
        {
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            requestBody = await reader.ReadToEndAsync();
            request.Body.Position = 0;
        }

        var originalBodyStream = context.Response.Body;
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        var start = DateTime.UtcNow;
        await next(context);
        var elapsedMs = (DateTime.UtcNow - start).TotalMilliseconds;

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        string responseText = await new StreamReader(context.Response.Body).ReadToEndAsync();
        context.Response.Body.Seek(0, SeekOrigin.Begin);

        var logObj = new
        {
            app = context.Request.Host.HasValue ? context.Request.Host.Value : "unknown",
            http = new
            {
                method = request.Method,
                path = request.Path.ToString(),
                query = request.QueryString.HasValue ? request.QueryString.Value : "",
                status = context.Response.StatusCode,
                elapsed_ms = elapsedMs
            },
            request = TryParseJson(requestBody),
            response = TryParseJson(responseText),
            traceId = context.TraceIdentifier
        };
        _logger.LogInformation("{Log}", JsonSerializer.Serialize(logObj));

        await responseBody.CopyToAsync(originalBodyStream);
    }

    private static object? TryParseJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            return JsonSerializer.Deserialize<object>(text);
        }
        catch
        {
            return text;
        }
    }
}
