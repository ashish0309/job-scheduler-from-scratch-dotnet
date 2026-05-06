using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace JobSchedulerPrototype.Jobs;

public sealed class JobSchedulerDbContext : DbContext, IDataAccessPolicyContext
{
    private static readonly IDataAccessPolicyFilterBuilder DefaultDataAccessPolicyFilterBuilder =
        new DataAccessPolicyFilterBuilder([new JobDataAccessPolicy()]);

    public JobSchedulerDbContext(
        DbContextOptions<JobSchedulerDbContext> options,
        IDataAccessScopeProvider? dataAccessScopeProvider = null,
        IDataAccessPolicyFilterBuilder? dataAccessPolicyFilterBuilder = null)
        : base(options)
    {
        _dataAccessScopeProvider = dataAccessScopeProvider
            ?? new FixedDataAccessScopeProvider(DataAccessScope.AllTenants());
        _dataAccessPolicyFilterBuilder = dataAccessPolicyFilterBuilder
            ?? DefaultDataAccessPolicyFilterBuilder;
    }

    private readonly IDataAccessScopeProvider _dataAccessScopeProvider;
    private readonly IDataAccessPolicyFilterBuilder _dataAccessPolicyFilterBuilder;

    public DbSet<JobRecord> Jobs => Set<JobRecord>();

    public DbSet<JobStateChange> JobStateChanges => Set<JobStateChange>();

    public JobActor Actor => _dataAccessScopeProvider.CurrentActor;

    public DataAccessScope Scope => _dataAccessScopeProvider.Current;

    public DataAccessOperation Operation => DataAccessOperation.Read;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JobRecord>(job =>
        {
            job.ToTable("Jobs");
            job.HasKey(entity => entity.Id);

            job.Property(entity => entity.TenantId)
                .IsRequired()
                .HasMaxLength(200);

            job.Property(entity => entity.CreatedByActorId)
                .IsRequired()
                .HasMaxLength(200);

            job.Property(entity => entity.Type)
                .IsRequired()
                .HasMaxLength(200);

            job.Property(entity => entity.Payload)
                .HasConversion(
                    payload => payload.GetRawText(),
                    json => JsonDocument.Parse(json, default).RootElement.Clone())
                .IsRequired();

            job.Property(entity => entity.Status)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(50);

            job.Property(entity => entity.MaxAttempts)
                .IsRequired();

            job.Property(entity => entity.RunAt)
                .HasConversion(
                    runAt => runAt.HasValue
                        ? runAt.Value.UtcDateTime.Ticks
                        : (long?)null,
                    ticks => ticks.HasValue
                        ? new DateTimeOffset(new DateTime(ticks.Value, DateTimeKind.Utc))
                        : null);

            job.Property(entity => entity.ClaimedBy)
                .HasMaxLength(200);

            job.Property(entity => entity.ClaimedAt)
                .HasConversion(
                    claimedAt => claimedAt.HasValue
                        ? claimedAt.Value.UtcDateTime.Ticks
                        : (long?)null,
                    ticks => ticks.HasValue
                        ? new DateTimeOffset(new DateTime(ticks.Value, DateTimeKind.Utc))
                        : null);

            job.Property(entity => entity.LeaseExpiresAt)
                .HasConversion(
                    leaseExpiresAt => leaseExpiresAt.HasValue
                        ? leaseExpiresAt.Value.UtcDateTime.Ticks
                        : (long?)null,
                    ticks => ticks.HasValue
                        ? new DateTimeOffset(new DateTime(ticks.Value, DateTimeKind.Utc))
                        : null);

            job.Property(entity => entity.FailureReason)
                .HasMaxLength(1000);

            job.Ignore(entity => entity.Attempts);
            job.Ignore(entity => entity.EnqueuedAt);
            job.Ignore(entity => entity.ScheduledAt);
            job.Ignore(entity => entity.StartedAt);
            job.Ignore(entity => entity.CompletedAt);
            job.Ignore(entity => entity.FailedAt);
            job.Ignore(entity => entity.AttemptCount);

            job.HasMany(entity => entity.History)
                .WithOne()
                .HasForeignKey("JobId")
                .OnDelete(DeleteBehavior.Cascade);

            job.Navigation(entity => entity.History)
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            job.HasIndex(entity => new { entity.Status, entity.RunAt });
            job.HasIndex(entity => entity.TenantId);
        });

        modelBuilder.Entity<JobStateChange>(stateChange =>
        {
            stateChange.ToTable("JobStateChanges");
            stateChange.HasKey(entity => entity.Id);

            stateChange.Property(entity => entity.Status)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(50);

            stateChange.Property(entity => entity.ChangedAt)
                .IsRequired();

            stateChange.Property(entity => entity.Reason)
                .IsRequired()
                .HasMaxLength(1000);

            stateChange.Property(entity => entity.Sequence)
                .IsRequired();

            stateChange.Ignore(entity => entity.Details);

            stateChange.HasIndex("JobId", nameof(JobStateChange.Sequence));
        });

        ApplyDataAccessPolicyQueryFilters(modelBuilder);
    }

    private void ApplyDataAccessPolicyQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var filter = _dataAccessPolicyFilterBuilder.BuildFilter(entityType.ClrType, this);
            if (filter is not null)
            {
                modelBuilder.Entity(entityType.ClrType)
                    .HasQueryFilter(filter);

                continue;
            }

            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                throw new InvalidOperationException(
                    $"Tenant-scoped entity {entityType.ClrType.Name} must register a data access policy.");
            }
        }
    }
}
