using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Observability.Logging;

public class RequestResponseLoggingMiddleware : IMiddleware
{
    private readonly ILogger<RequestResponseLoggingMiddleware> _logger;
    private readonly string _serviceName;
    public RequestResponseLoggingMiddleware(ILogger<RequestResponseLoggingMiddleware> logger, string serviceName)
    {
        _logger = logger;
        _serviceName = string.IsNullOrWhiteSpace(serviceName) ? "unknown-service" : serviceName;
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

        var errorKey = ExtractErrorKey(responseText);
        var statusCode = context.Response.StatusCode;
        var method = request.Method;
        var path = request.Path.ToString();
        var query = request.QueryString.HasValue ? request.QueryString.Value : "";
        var traceId = context.TraceIdentifier;
        _logger.LogInformation(
            "http_request service={service} status_code={status_code} error_key={error_key} method={method} path={path} query={query} elapsed_ms={elapsed_ms} traceId={traceId} request={request} response={response}",
            _serviceName,
            statusCode,
            errorKey ?? "",
            method,
            path,
            query,
            elapsedMs,
            traceId,
            string.IsNullOrWhiteSpace(requestBody) ? "" : requestBody,
            string.IsNullOrWhiteSpace(responseText) ? "" : responseText
        );

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

    private static string? ExtractErrorKey(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("error_key", out var ek) && ek.ValueKind == JsonValueKind.String) return ek.GetString();
                if (root.TryGetProperty("errorKey", out var ek2) && ek2.ValueKind == JsonValueKind.String) return ek2.GetString();
                if (root.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String) return code.GetString();
                if (root.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String) return key.GetString();
                if (root.TryGetProperty("error", out var err))
                {
                    if (err.ValueKind == JsonValueKind.Object)
                    {
                        if (err.TryGetProperty("key", out var ek3) && ek3.ValueKind == JsonValueKind.String) return ek3.GetString();
                        if (err.TryGetProperty("code", out var c2) && c2.ValueKind == JsonValueKind.String) return c2.GetString();
                    }
                    if (err.ValueKind == JsonValueKind.String) return err.GetString();
                }
                if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in errors.EnumerateArray())
                    {
                        if (e.ValueKind == JsonValueKind.Object)
                        {
                            if (e.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String) return k.GetString();
                            if (e.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String) return c.GetString();
                        }
                    }
                }
            }
        }
        catch
        {
        }
        return null;
    }
}
