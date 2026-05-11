using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AgentHost.Observability;

/// <summary>
/// Configuration options for AgentHost observability (OTel + Application Insights).
/// </summary>
public sealed class ObservabilityOptions
{
    public OtelOptions Otel { get; set; } = new();
    public AppInsightsOptions ApplicationInsights { get; set; } = new();
}

public sealed class OtelOptions
{
    public bool Enabled { get; set; } = true;
    public string ServiceName { get; set; } = "agenthost";
    /// <summary>"Otlp", "Console", or "Both"</summary>
    public string Exporter { get; set; } = "Otlp";
    public string? OtlpEndpoint { get; set; }
    public bool ConsoleExporter { get; set; } = false;
}

public sealed class AppInsightsOptions
{
    public string? ConnectionString { get; set; }
}

/// <summary>
/// Extension that wires OpenTelemetry tracing, metrics and logging for AgentHost.
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// Registers the AgentHost observability stack (OTel traces + metrics + optional App Insights).
    /// </summary>
    public static IServiceCollection AddAgentHostObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var opts = new ObservabilityOptions();
        configuration.GetSection("Observability").Bind(opts);

        // ── App Insights via OTel (preferred) ───────────────────────────────
        var aiConnStr = opts.ApplicationInsights.ConnectionString
            ?? configuration["ApplicationInsights:ConnectionString"];

        if (!string.IsNullOrWhiteSpace(aiConnStr))
        {
            services.AddOpenTelemetry().UseAzureMonitor(o =>
            {
                o.ConnectionString = aiConnStr;
            });
        }

        // ── OpenTelemetry ────────────────────────────────────────────────────
        if (!opts.Otel.Enabled)
            return services;

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: opts.Otel.ServiceName,
                serviceVersion: AgentHostActivitySource.Version)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
            });

        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation(o =>
                    {
                        o.RecordException = true;
                    })
                    .AddHttpClientInstrumentation(o =>
                    {
                        o.RecordException = true;
                    })
                    .AddSqlClientInstrumentation(o =>
                    {
                        o.RecordException = true;
                    })
                    .AddSource(AgentHostActivitySource.Name)
                    .AddSource("AgentHost.Llm");

                ConfigureTraceExporters(tracing, opts);
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddMeter(AgentHostMetrics.MeterName)
                    .AddMeter("AgentHost.Llm"); // TokenAccountant meter

                ConfigureMetricExporters(metrics, opts);
            });

        // W3C trace context propagation is enabled by default in OTel .NET SDK
        // and ASP.NET Core instrumentation handles traceparent header propagation.

        return services;
    }

    private static void ConfigureTraceExporters(TracerProviderBuilder tracing, ObservabilityOptions opts)
    {
        var exporter = opts.Otel.Exporter;

        if (exporter.Equals("Otlp", StringComparison.OrdinalIgnoreCase) ||
            exporter.Equals("Both", StringComparison.OrdinalIgnoreCase))
        {
            tracing.AddOtlpExporter(o =>
            {
                if (!string.IsNullOrWhiteSpace(opts.Otel.OtlpEndpoint))
                    o.Endpoint = new Uri(opts.Otel.OtlpEndpoint);
                o.Protocol = OtlpExportProtocol.Grpc;
            });
        }

        if (opts.Otel.ConsoleExporter ||
            exporter.Equals("Console", StringComparison.OrdinalIgnoreCase) ||
            exporter.Equals("Both", StringComparison.OrdinalIgnoreCase))
        {
            tracing.AddConsoleExporter();
        }
    }

    private static void ConfigureMetricExporters(MeterProviderBuilder metrics, ObservabilityOptions opts)
    {
        var exporter = opts.Otel.Exporter;

        if (exporter.Equals("Otlp", StringComparison.OrdinalIgnoreCase) ||
            exporter.Equals("Both", StringComparison.OrdinalIgnoreCase))
        {
            metrics.AddOtlpExporter(o =>
            {
                if (!string.IsNullOrWhiteSpace(opts.Otel.OtlpEndpoint))
                    o.Endpoint = new Uri(opts.Otel.OtlpEndpoint);
                o.Protocol = OtlpExportProtocol.Grpc;
            });
        }

        if (opts.Otel.ConsoleExporter ||
            exporter.Equals("Console", StringComparison.OrdinalIgnoreCase) ||
            exporter.Equals("Both", StringComparison.OrdinalIgnoreCase))
        {
            metrics.AddConsoleExporter();
        }
    }
}
