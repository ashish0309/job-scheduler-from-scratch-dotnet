using Microsoft.EntityFrameworkCore;

namespace JobSchedulerPrototype.Jobs;

public sealed class JobSchedulerDbContext : DbContext
{
    public JobSchedulerDbContext(DbContextOptions<JobSchedulerDbContext> options)
        : base(options)
    {
    }

    public DbSet<JobEntity> Jobs => Set<JobEntity>();

    public DbSet<JobStateChangeEntity> JobStateChanges => Set<JobStateChangeEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JobEntity>(job =>
        {
            job.ToTable("Jobs");
            job.HasKey(entity => entity.Id);

            job.Property(entity => entity.Status)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(JobEntity.StatusMaxLength);

            job.Property(entity => entity.MaxAttempts)
                .IsRequired();

            job.HasMany(entity => entity.History)
                .WithOne(entity => entity.Job)
                .HasForeignKey(entity => entity.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JobStateChangeEntity>(stateChange =>
        {
            stateChange.ToTable("JobStateChanges");
            stateChange.HasKey(entity => entity.Id);

            stateChange.Property(entity => entity.Status)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(JobStateChangeEntity.StatusMaxLength);

            stateChange.Property(entity => entity.ChangedAt)
                .IsRequired();

            stateChange.HasIndex(entity => new
            {
                entity.JobId,
                entity.ChangedAt
            });
        });
    }
}
