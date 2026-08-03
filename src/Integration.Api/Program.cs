using System.Net;
using FluentValidation;
using Integration.Api.Middleware;
using Integration.Api.Validators;
using Integration.Shared.Clients;
using Integration.Shared.Configuration;
using Integration.Shared.Connectors.HansaCrm;
using Integration.Shared.Infrastructure;
using Integration.Shared.Repositories;
using Integration.Shared.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Refit;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// Logging: Serilog → Seq
// ============================================================================
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();

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

// ============================================================================
// Observability: OpenTelemetry
// ============================================================================
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Integration.Api"));
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddHttpClientInstrumentation();
        tracing.AddSource("MassTransit");
        tracing.AddOtlpExporter(opt =>
        {
            opt.Endpoint = new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://otel-collector:4317");
        });
    })
    .WithMetrics(metrics =>
    {
        metrics.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Integration.Api"));
        metrics.AddAspNetCoreInstrumentation();
        metrics.AddPrometheusExporter();
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
    return new HanaConnectionPool(hanaConfig.ConnectionString, maxSize: 3, logger);
});

// ============================================================================
// HTTP Clients + Resilience (Polly v8) — isolated per-tenant pipelines
// ============================================================================

// ============================================================================
// Repositories and Clients
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
builder.Services.AddScoped<MetricRepository>();
builder.Services.AddScoped<TenantQuotaRepository>();
builder.Services.AddScoped<IntegrationRequestRepository>();
builder.Services.AddSingleton<ITenantFeatureService, TenantFeatureService>();
builder.Services.AddMemoryCache();

// ============================================================================
// Messaging: MassTransit + RabbitMQ
// ============================================================================
builder.Services.AddMassTransit(x =>
{
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

        cfg.ConfigureEndpoints(context);
    });
});

// ============================================================================
// Validation + API
// ============================================================================
builder.Services.AddValidatorsFromAssemblyContaining<CrmOrderPayloadValidator>();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ============================================================================
// Health Checks
// ============================================================================
builder.Services.AddHealthChecks()
    .AddCheck<Integration.Api.HealthChecks.PostgresHealthCheck>("postgres")
    .AddCheck<Integration.Api.HealthChecks.SapHealthCheck>("sap");

var app = builder.Build();

// ============================================================================
// Pipeline HTTP
// ============================================================================
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseMiddleware<TenantMiddleware>();
app.UseMiddleware<TenantRateLimitMiddleware>();

app.UseAuthorization();
app.MapControllers();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false // Only checks that the process is alive
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = reg => reg.Tags.Contains("ready") || true // Verifica todas las dependencias
});

// Prometheus metrics scraping endpoint
app.MapPrometheusScrapingEndpoint();

// Apply migrations on startup (Phase 1; in production use init containers)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
    await db.Database.MigrateAsync();
}

await app.RunAsync();
