using Integration.Shared.Domain;
using Microsoft.EntityFrameworkCore;
using EFCore.NamingConventions;

namespace Integration.Shared.Infrastructure;

/// <summary>
/// EF Core DbContext for PostgreSQL. Manages the local outbox,
/// integration logs, idempotency and tenant configuration.
/// </summary>
public class IntegrationDbContext : DbContext
{
    public IntegrationDbContext(DbContextOptions<IntegrationDbContext> options) : base(options) { }

    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<IntegrationLog> IntegrationLogs => Set<IntegrationLog>();
    public DbSet<TenantConfig> TenantConfigs => Set<TenantConfig>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<DeadLetterEvent> DeadLetterEvents => Set<DeadLetterEvent>();
    public DbSet<TenantFeatureFlag> TenantFeatureFlags => Set<TenantFeatureFlag>();
    public DbSet<IntegrationAlert> Alerts => Set<IntegrationAlert>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<PriceSnapshot> PriceSnapshots => Set<PriceSnapshot>();
    public DbSet<PollingCursor> PollingCursors => Set<PollingCursor>();
    public DbSet<IntegrationMetricCounter> MetricCounters => Set<IntegrationMetricCounter>();
    public DbSet<TenantQuota> TenantQuotas => Set<TenantQuota>();
    public DbSet<IntegrationRequest> IntegrationRequests => Set<IntegrationRequest>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseSnakeCaseNamingConvention();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // OutboxEvent
        modelBuilder.Entity<OutboxEvent>(entity =>
        {
            entity.ToTable("outbox_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(128).IsRequired();
            entity.Property(e => e.AggregateId).HasMaxLength(128).IsRequired();
            entity.HasIndex(e => new { e.ProcessedAt, e.AttemptCount });
            entity.HasIndex(e => new { e.TenantId, e.AggregateId });
        });

        // IntegrationLog
        modelBuilder.Entity<IntegrationLog>(entity =>
        {
            entity.ToTable("integration_logs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(128).IsRequired();
            entity.Property(e => e.ExternalId).HasMaxLength(128);
            entity.Property(e => e.SapDocEntry).HasMaxLength(64);
            entity.Property(e => e.Status).HasMaxLength(32).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.CorrelationId });
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.CreatedAt, e.Status });
            entity.HasIndex(e => new { e.CreatedAt, e.TenantId });
        });

        // TenantConfig
        modelBuilder.Entity<TenantConfig>(entity =>
        {
            entity.ToTable("tenant_config");
            entity.HasKey(e => e.TenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64);
            entity.Property(e => e.Name).HasMaxLength(256).IsRequired();
            entity.Property(e => e.ApiKeyHash).HasMaxLength(256).IsRequired();
            entity.Property(e => e.CrmConnectorType).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(e => e.IsActive);
        });

        // ProcessedMessage
        modelBuilder.Entity<ProcessedMessage>(entity =>
        {
            entity.ToTable("processed_messages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MessageId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Consumer).HasMaxLength(128).IsRequired();
            entity.HasIndex(e => new { e.MessageId, e.Consumer }).IsUnique();
        });

        // DeadLetterEvent
        modelBuilder.Entity<DeadLetterEvent>(entity =>
        {
            entity.ToTable("dead_letter_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Source).HasMaxLength(64).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(128).IsRequired();
            entity.Property(e => e.AggregateId).HasMaxLength(128).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.DeadLetteredAt });
        });

        // TenantFeatureFlag
        modelBuilder.Entity<TenantFeatureFlag>(entity =>
        {
            entity.ToTable("tenant_feature_flags");
            entity.HasKey(e => new { e.TenantId, e.FeatureKey });
            entity.Property(e => e.TenantId).HasMaxLength(64);
            entity.Property(e => e.FeatureKey).HasMaxLength(128);
            entity.Property(e => e.UpdatedAt).IsRequired();
            entity.HasIndex(e => e.TenantId);
        });

        // IntegrationAlert
        modelBuilder.Entity<IntegrationAlert>(entity =>
        {
            entity.ToTable("integration_alerts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Title).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Message).HasMaxLength(1024).IsRequired();
            entity.Property(e => e.Details).HasMaxLength(4096);
            entity.HasIndex(e => new { e.TenantId, e.IsAcknowledged, e.CreatedAt });
            entity.HasIndex(e => e.AlertType);
        });

        // IdempotencyRecord
        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("idempotency_records");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(128).IsRequired();
            entity.Property(e => e.AggregateId).HasMaxLength(256).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.EventType, e.AggregateId }).IsUnique();
            entity.HasIndex(e => e.ExpiresAt);
        });

        // PriceSnapshot
        modelBuilder.Entity<PriceSnapshot>(entity =>
        {
            entity.ToTable("price_snapshots");
            entity.HasKey(e => new { e.TenantId, e.ItemCode, e.PriceList });
            entity.Property(e => e.TenantId).HasMaxLength(64);
            entity.Property(e => e.ItemCode).HasMaxLength(64);
            entity.Property(e => e.Currency).HasMaxLength(16);
            entity.Property(e => e.PriceHash).HasMaxLength(64);
        });

        // PollingCursor
        modelBuilder.Entity<PollingCursor>(entity =>
        {
            entity.ToTable("polling_cursors");
            entity.HasKey(e => new { e.TenantId, e.EntityType });
            entity.Property(e => e.TenantId).HasMaxLength(64);
            entity.Property(e => e.EntityType).HasMaxLength(64);
        });

        // IntegrationMetricCounter
        modelBuilder.Entity<IntegrationMetricCounter>(entity =>
        {
            entity.ToTable("integration_metric_counters");
            entity.HasKey(e => e.MetricKey);
            entity.Property(e => e.MetricKey).HasMaxLength(128);
            entity.Property(e => e.MetricValue).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();
        });

        // TenantQuota
        modelBuilder.Entity<TenantQuota>(entity =>
        {
            entity.ToTable("tenant_quotas");
            entity.HasKey(e => e.TenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64);
            entity.Property(e => e.MaxEventsPerHour).IsRequired();
            entity.Property(e => e.MaxApiCallsPerMinute).IsRequired();
        });

        // IntegrationRequest
        modelBuilder.Entity<IntegrationRequest>(entity =>
        {
            entity.ToTable("integration_requests");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.SourceSystem).HasMaxLength(64).IsRequired();
            entity.Property(e => e.TargetSystem).HasMaxLength(64).IsRequired();
            entity.Property(e => e.EntityType).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Operation).HasMaxLength(32).IsRequired();
            entity.Property(e => e.ExternalId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(32).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.Status, e.NextRetryAt });
            entity.HasIndex(e => new { e.TenantId, e.ExternalId, e.EntityType });
            entity.HasIndex(e => new { e.Status, e.LeasedUntil });
            entity.HasIndex(e => e.ReceivedAt);
        });
    }
}
