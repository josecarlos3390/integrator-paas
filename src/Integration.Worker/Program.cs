using System.Net;
using Integration.Shared.Clients;
using Integration.Shared.Configuration;
using Integration.Shared.Connectors.HansaCrm;
using Integration.Shared.Infrastructure;
using Integration.Shared.Repositories;
using Integration.Shared.Services;
using Integration.Worker.Dispatchers;
using Integration.Worker.Workers;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Refit;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// ============================================================================
// Logging: Serilog → Seq
// ============================================================================
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

// ============================================================================
// Configuration
// ============================================================================
builder.Services.Configure<SapConfig>(builder.Configuration.GetSection("Sap"));
builder.Services.Configure<HanaConfig>(builder.Configuration.GetSection("Hana"));
builder.Services.Configure<PostgresConfig>(builder.Configuration.GetSection("Postgres"));
builder.Services.Configure<CrmConfig>(builder.Configuration.GetSection("Crm"));
builder.Services.Configure<HansaCrmConfig>(builder.Configuration.GetSection("HansaCrm"));
builder.Services.Configure<OutboxConfig>(builder.Configuration.GetSection("Outbox"));
builder.Services.Configure<AlertingConfig>(builder.Configuration.GetSection("Alerting"));
builder.Services.Configure<IdempotencyConfig>(builder.Configuration.GetSection("Idempotency"));
builder.Services.Configure<TenantsConfig>(builder.Configuration.GetSection("Tenants"));
builder.Services.Configure<IngestionConfig>(builder.Configuration.GetSection("Ingestion"));
builder.Services.Configure<PriceListPollingConfig>(builder.Configuration.GetSection("PriceListPolling"));

// ============================================================================
// Observability: OpenTelemetry
// ============================================================================
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Integration.Worker"));
        tracing.AddHttpClientInstrumentation();
        tracing.AddSource("MassTransit");
        tracing.AddOtlpExporter(opt =>
        {
            opt.Endpoint = new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://otel-collector:4317");
        });
    })
    .WithMetrics(metrics =>
    {
        metrics.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Integration.Worker"));
        metrics.AddOtlpExporter(opt =>
        {
            opt.Endpoint = new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://otel-collector:4317");
        });
    });

// ============================================================================
// PostgreSQL + EF Core
// ============================================================================
builder.Services.AddDbContext<IntegrationDbContext>((sp, opt) =>
{
    var pg = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PostgresConfig>>().Value;
    opt.UseNpgsql(pg.ConnectionString);
});

// ============================================================================
// HANA Connection Pool (singleton — avoids TCP overhead per query)
// ============================================================================
builder.Services.AddSingleton(sp =>
{
    var hanaConfig = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HanaConfig>>().Value;
    var logger = sp.GetRequiredService<ILogger<HanaConnectionPool>>();
    return new HanaConnectionPool(hanaConfig.ConnectionString, maxSize: 5, logger);
});
// Registry of all configured HANA servers for multi-HANA outbox polling
builder.Services.AddSingleton<HanaConnectionPoolRegistry>();

// ============================================================================
// HTTP Clients + Resilience (Polly v8) — isolated per-tenant pipelines
// ============================================================================

// ============================================================================
// Repositories, Clients and Dispatcher
// ============================================================================
builder.Services.AddHttpClient("sap-base")
    .ConfigurePrimaryHttpMessageHandler(sp => new System.Net.Http.SocketsHttpHandler
    {
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) =>
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SapConfig>>().Value.ValidateCertificates
                || sslPolicyErrors == System.Net.Security.SslPolicyErrors.None
        },
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        MaxConnectionsPerServer = 10
    });

builder.Services.AddHttpClient("crm-base")
    .ConfigurePrimaryHttpMessageHandler(sp => new System.Net.Http.SocketsHttpHandler
    {
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) =>
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HansaCrmConfig>>().Value.ValidateCertificates
                || sslPolicyErrors == System.Net.Security.SslPolicyErrors.None
        },
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        MaxConnectionsPerServer = 10
    });

builder.Services.AddHttpClient();
builder.Services.AddSingleton<ITenantClientFactory, TenantClientFactory>();
builder.Services.AddScoped<HanaOutboxRepository>();
builder.Services.AddScoped<AlertRepository>();
builder.Services.AddScoped<IAlertingService, AlertingService>();
builder.Services.AddScoped<IdempotencyRepository>();
builder.Services.AddScoped<IIdempotencyService, IdempotencyService>();
builder.Services.AddScoped<IntegrationLogRepository>();
builder.Services.AddScoped<DeadLetterRepository>();
builder.Services.AddScoped<TenantConfigRepository>();
builder.Services.AddScoped<TenantFeatureFlagRepository>();
builder.Services.AddSingleton<ITenantFeatureService, TenantFeatureService>();
builder.Services.AddScoped<PriceSnapshotRepository>();
builder.Services.AddScoped<PollingCursorRepository>();
builder.Services.AddScoped<MetricRepository>();
builder.Services.AddScoped<TenantQuotaRepository>();
builder.Services.AddScoped<IntegrationRequestRepository>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<HanaOutboxDispatcher>();
builder.Services.AddSingleton<Integration.Worker.Services.IRequestRouter, Integration.Worker.Services.RequestRouter>();
builder.Services.AddScoped<Integration.Worker.Services.IngestionProcessor>();
builder.Services.AddSingleton<Integration.Worker.Services.CallbackNotifier>();

// ============================================================================
// Messaging: MassTransit + RabbitMQ + Consumer
// ============================================================================
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<CrmOrderWorker>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var host = builder.Configuration["RabbitMQ:Host"] ?? "rabbitmq";
        var user = builder.Configuration["RabbitMQ:Username"] ?? "guest";
        var pass = builder.Configuration["RabbitMQ:Password"] ?? "guest";

        cfg.Host(host, "/", h =>
        {
            h.Username(user);
            h.Password(pass);
        });

        cfg.ReceiveEndpoint("crm-order-queue", e =>
        {
            e.PrefetchCount = 20;
            e.ConcurrentMessageLimit = 10;
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
            e.ConfigureConsumer<CrmOrderWorker>(context);
        });

        cfg.ConfigureEndpoints(context);
    });
});

// ============================================================================
// Background Services
// ============================================================================
builder.Services.AddHostedService<OutboxDispatcherWorker>();
builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddHostedService<DlqRetryWorker>();
builder.Services.AddHostedService<AlertingWorker>();
builder.Services.AddHostedService<IdempotencyCleanupWorker>();
builder.Services.AddHostedService<PriceListPollingWorker>();
builder.Services.AddHostedService<LogRetentionWorker>();

var host = builder.Build();
await host.RunAsync();
