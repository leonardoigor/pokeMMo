using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Logs;
using OpenTelemetry.Exporter;
using Elastic.Extensions.Logging;
using Observability.Logging;

namespace Observability.Extensions;

public static class ObservabilityExtensions
{
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration config, string serviceName)
    {
        var endpoint = config["OTEL:Endpoint"] ?? "http://otel-collector:4318";
        services.AddOpenTelemetry()
            .ConfigureResource(rb => rb.AddService(serviceName))
            .WithTracing(builder =>
            {
                builder
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.Filter = ctx => true;
                    })
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(endpoint);
                    });
            })
            .WithMetrics(builder =>
            {
                builder
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(endpoint);
                    });
            })
            ;

        services.AddLogging(lb =>
        {
            lb.AddJsonConsole();
            lb.AddElasticsearch(options =>
            {
                var section = config.GetSection("Logging:Elasticsearch");
                if (section.Exists()) section.Bind(options);
            });
        });

        services.AddSingleton<RequestResponseLoggingMiddleware>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<RequestResponseLoggingMiddleware>>();
            return new RequestResponseLoggingMiddleware(logger, serviceName);
        });
        return services;
    }

    public static IApplicationBuilder UseRequestResponseLogging(this IApplicationBuilder app)
        => app.UseMiddleware<RequestResponseLoggingMiddleware>();
}
