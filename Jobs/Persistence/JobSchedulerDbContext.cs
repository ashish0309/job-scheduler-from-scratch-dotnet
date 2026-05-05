using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace JobSchedulerPrototype.Jobs;

public sealed class JobSchedulerDbContext : DbContext
{
    public JobSchedulerDbContext(DbContextOptions<JobSchedulerDbContext> options)
        : base(options)
    {
    }

    public DbSet<JobRecord> Jobs => Set<JobRecord>();

    public DbSet<JobStateChange> JobStateChanges => Set<JobStateChange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JobRecord>(job =>
        {
            job.ToTable("Jobs");
            job.HasKey(entity => entity.Id);

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
    }
}
