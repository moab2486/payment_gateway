using CardManagement.Domain.Entities;
using CardManagement.Domain.PlatformServices.AdminConsole;
using CardManagement.Domain.PlatformServices.AdminConsole.ReadModels;
using CardManagement.Domain.PlatformServices.DeveloperPortal;
using CardManagement.Domain.PlatformServices.Notifications;
using CardManagement.Domain.PlatformServices.Reconciliation;
using CardManagement.Domain.PlatformServices.Webhooks;
using CardManagement.Infrastructure.Idempotency;
using CardManagement.Infrastructure.Persistence.Configurations;
using CardManagement.Infrastructure.PlatformServices.AdminConsole.Persistence;
using CardManagement.Infrastructure.PlatformServices.DeveloperPortal.Persistence;
using CardManagement.Infrastructure.PlatformServices.Notifications.Persistence;
using CardManagement.Infrastructure.PlatformServices.Reconciliation.Configurations;
using CardManagement.Infrastructure.PlatformServices.Webhooks.Persistence;
using CardManagement.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Card Management System.
/// Configures PostgreSQL via Npgsql with GUID primary keys, UTC timestamps,
/// and immutable LedgerEntry mapping.
/// </summary>
public class CardManagementDbContext : DbContext
{
    public CardManagementDbContext(DbContextOptions<CardManagementDbContext> options)
        : base(options)
    {
    }

    public DbSet<Card> Cards => Set<Card>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<TransactionRecord> TransactionRecords => Set<TransactionRecord>();
    public DbSet<ProcessorSession> ProcessorSessions => Set<ProcessorSession>();
    public DbSet<IdempotencyRecordEntity> IdempotencyRecords => Set<IdempotencyRecordEntity>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<SagaState> SagaStates => Set<SagaState>();
    public DbSet<TokenVaultEntity> TokenVaultEntries => Set<TokenVaultEntity>();
    public DbSet<PaymentRequest> PaymentRequests => Set<PaymentRequest>();
    public DbSet<DirectDebitMandate> DirectDebitMandates => Set<DirectDebitMandate>();
    public DbSet<DisputeRecord> DisputeRecords => Set<DisputeRecord>();

    // Reconciliation
    public DbSet<ReconciliationBatch> ReconciliationBatches => Set<ReconciliationBatch>();
    public DbSet<SettlementLineItem> SettlementLineItems => Set<SettlementLineItem>();
    public DbSet<ReconciliationException> ReconciliationExceptions => Set<ReconciliationException>();
    public DbSet<Adjustment> Adjustments => Set<Adjustment>();

    // Webhooks
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<DlqItem> WebhookDlqItems => Set<DlqItem>();

    // Notifications
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<DeliveryLogEntry> NotificationDeliveryLogs => Set<DeliveryLogEntry>();

    // Admin Console
    public DbSet<PendingCommand> AdminPendingCommands => Set<PendingCommand>();
    public DbSet<AdminRole> AdminRoles => Set<AdminRole>();
    public DbSet<TransactionSummaryReadModel> AdminTransactionSummaries => Set<TransactionSummaryReadModel>();
    public DbSet<ReconciliationStatusReadModel> AdminReconciliationStatuses => Set<ReconciliationStatusReadModel>();
    public DbSet<DisputeMetricsReadModel> AdminDisputeMetrics => Set<DisputeMetricsReadModel>();
    public DbSet<ChannelHealthReadModel> AdminChannelHealth => Set<ChannelHealthReadModel>();

    // Developer Portal
    public DbSet<ApiKey> DeveloperApiKeys => Set<ApiKey>();
    public DbSet<RequestLogEntry> DeveloperRequestLogs => Set<RequestLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new CardConfiguration());
        modelBuilder.ApplyConfiguration(new AccountConfiguration());
        modelBuilder.ApplyConfiguration(new LedgerEntryConfiguration());
        modelBuilder.ApplyConfiguration(new TransactionRecordConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessorSessionConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration());
        modelBuilder.ApplyConfiguration(new AuditEntryConfiguration());
        modelBuilder.ApplyConfiguration(new SagaStateConfiguration());
        modelBuilder.ApplyConfiguration(new TokenVaultEntityConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentRequestConfiguration());
        modelBuilder.ApplyConfiguration(new DirectDebitMandateConfiguration());
        modelBuilder.ApplyConfiguration(new DisputeRecordConfiguration());

        // Reconciliation configurations
        modelBuilder.ApplyConfiguration(new ReconciliationBatchConfiguration());
        modelBuilder.ApplyConfiguration(new SettlementLineItemConfiguration());
        modelBuilder.ApplyConfiguration(new ReconciliationExceptionConfiguration());
        modelBuilder.ApplyConfiguration(new AdjustmentConfiguration());

        // Webhook configurations
        modelBuilder.ApplyConfiguration(new WebhookSubscriptionConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookDeliveryConfiguration());
        modelBuilder.ApplyConfiguration(new DlqItemConfiguration());

        // Notification configurations
        modelBuilder.ApplyConfiguration(new NotificationTemplateConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationPreferenceConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryLogEntryConfiguration());

        // Admin Console configurations
        modelBuilder.ApplyConfiguration(new PendingCommandConfiguration());
        modelBuilder.ApplyConfiguration(new AdminRoleConfiguration());
        modelBuilder.ApplyConfiguration(new TransactionSummaryReadModelConfiguration());
        modelBuilder.ApplyConfiguration(new ReconciliationStatusReadModelConfiguration());
        modelBuilder.ApplyConfiguration(new DisputeMetricsReadModelConfiguration());
        modelBuilder.ApplyConfiguration(new ChannelHealthReadModelConfiguration());

        // Developer Portal configurations
        modelBuilder.ApplyConfiguration(new ApiKeyConfiguration());
        modelBuilder.ApplyConfiguration(new RequestLogEntryConfiguration());
    }

    /// <summary>
    /// Override SaveChangesAsync to enforce immutability for LedgerEntry and AuditEntry.
    /// These entities in Modified or Deleted state are rejected to maintain append-only semantics.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceImmutableEntries();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Override SaveChanges to enforce immutability for LedgerEntry and AuditEntry.
    /// </summary>
    public override int SaveChanges()
    {
        EnforceImmutableEntries();
        return base.SaveChanges();
    }

    /// <summary>
    /// Override SaveChanges with acceptAllChangesOnSuccess to enforce immutability.
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceImmutableEntries();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>
    /// Override SaveChangesAsync with acceptAllChangesOnSuccess to enforce immutability.
    /// </summary>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceImmutableEntries();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnforceImmutableEntries()
    {
        var modifiedLedgerEntries = ChangeTracker.Entries<LedgerEntry>()
            .Where(e => e.State == EntityState.Modified || e.State == EntityState.Deleted)
            .ToList();

        if (modifiedLedgerEntries.Count > 0)
        {
            throw new InvalidOperationException(
                "LedgerEntry entities are immutable and cannot be modified or deleted. " +
                "The ledger is an append-only audit trail.");
        }

        var modifiedAuditEntries = ChangeTracker.Entries<AuditEntry>()
            .Where(e => e.State == EntityState.Modified || e.State == EntityState.Deleted)
            .ToList();

        if (modifiedAuditEntries.Count > 0)
        {
            throw new InvalidOperationException(
                "AuditEntry entities are immutable and cannot be modified or deleted. " +
                "The audit trail is append-only.");
        }
    }
}
