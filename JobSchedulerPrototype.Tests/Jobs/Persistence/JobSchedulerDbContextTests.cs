using System.Text.Json;
using JobSchedulerPrototype.Jobs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class JobSchedulerDbContextTests
{
    [Fact]
    public void ModelConfiguresRequiredPropertiesAndMaxLengths()
    {
        var options = new DbContextOptionsBuilder<JobSchedulerDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var db = new JobSchedulerDbContext(options);

        var jobEntity = db.Model.FindEntityType(typeof(JobRecord));
        Assert.NotNull(jobEntity);
        Assert.False(jobEntity.FindProperty(nameof(JobRecord.TenantId))?.IsNullable);
        Assert.Equal(
            200,
            jobEntity.FindProperty(nameof(JobRecord.TenantId))?.GetMaxLength());
        Assert.False(jobEntity.FindProperty(nameof(JobRecord.CreatedByActorId))?.IsNullable);
        Assert.Equal(
            200,
            jobEntity.FindProperty(nameof(JobRecord.CreatedByActorId))?.GetMaxLength());
        Assert.False(jobEntity.FindProperty(nameof(JobRecord.Type))?.IsNullable);
        Assert.Equal(
            200,
            jobEntity.FindProperty(nameof(JobRecord.Type))?.GetMaxLength());
        Assert.False(jobEntity.FindProperty(nameof(JobRecord.Payload))?.IsNullable);
        Assert.False(jobEntity.FindProperty(nameof(JobRecord.Status))?.IsNullable);
        Assert.Equal(
            50,
            jobEntity.FindProperty(nameof(JobRecord.Status))?.GetMaxLength());
        Assert.NotNull(jobEntity.FindProperty(nameof(JobRecord.RunAt)));
        Assert.Equal(
            200,
            jobEntity.FindProperty(nameof(JobRecord.ClaimedBy))?.GetMaxLength());
        Assert.NotNull(jobEntity.FindProperty(nameof(JobRecord.ClaimedAt)));
        Assert.NotNull(jobEntity.FindProperty(nameof(JobRecord.LeaseExpiresAt)));
        Assert.NotNull(jobEntity.FindIndex(
            [
                jobEntity.FindProperty(nameof(JobRecord.Status))!,
                jobEntity.FindProperty(nameof(JobRecord.RunAt))!
            ]));
        Assert.NotNull(jobEntity.FindIndex(
            [jobEntity.FindProperty(nameof(JobRecord.TenantId))!]));
        Assert.NotNull(jobEntity.GetQueryFilter());
        Assert.Equal(
            1000,
            jobEntity.FindProperty(nameof(JobRecord.FailureReason))?.GetMaxLength());

        var stateChangeEntity = db.Model.FindEntityType(typeof(JobStateChange));
        Assert.NotNull(stateChangeEntity);
        Assert.False(stateChangeEntity.FindProperty(nameof(JobStateChange.Status))?.IsNullable);
        Assert.Equal(
            50,
            stateChangeEntity.FindProperty(nameof(JobStateChange.Status))?.GetMaxLength());
        Assert.False(stateChangeEntity.FindProperty(nameof(JobStateChange.Reason))?.IsNullable);
        Assert.Equal(
            1000,
            stateChangeEntity.FindProperty(nameof(JobStateChange.Reason))?.GetMaxLength());
        Assert.False(stateChangeEntity.FindProperty(nameof(JobStateChange.Sequence))?.IsNullable);
    }

    [Fact]
    public async Task TenantScopedQueriesOnlyReturnCurrentTenantJobs()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<JobSchedulerDbContext>()
            .UseSqlite(connection)
            .Options;
        var tenantJob = CreateQueuedJob(TestJobActorProvider.TenantId);
        var otherTenantJob = CreateQueuedJob("tenant-beta");

        await using (var db = CreateDbContext(options, DataAccessScope.AllTenants()))
        {
            await db.Database.EnsureCreatedAsync();
            db.Jobs.AddRange(tenantJob, otherTenantJob);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext(
            options,
            DataAccessScope.Tenant(TestJobActorProvider.TenantId)))
        {
            var jobs = await db.Jobs.ToArrayAsync();

            var job = Assert.Single(jobs);
            Assert.Equal(tenantJob.Id, job.Id);
            Assert.Null(await db.Jobs.SingleOrDefaultAsync(job => job.Id == otherTenantJob.Id));
        }

        await using (var db = CreateDbContext(options, DataAccessScope.AllTenants()))
        {
            Assert.Equal(2, await db.Jobs.CountAsync());
        }
    }

    [Fact]
    public async Task CanSaveAndReadJobWithHistory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<JobSchedulerDbContext>()
            .UseSqlite(connection)
            .Options;

        var jobId = Guid.NewGuid();
        var changedAt = new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero);
        var scheduledAt = changedAt.AddSeconds(30);
        var job = JobRecord.Schedule(
            jobId,
            TestJobActorProvider.TenantId,
            TestJobActorProvider.ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            scheduledAt,
            changedAt);

        await using (var db = new JobSchedulerDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
        }

        await using (var db = new JobSchedulerDbContext(options))
        {
            var persistedJob = await db.Jobs
                .Include(entity => entity.History)
                .SingleAsync(entity => entity.Id == jobId);

            Assert.Equal("send-welcome-email", persistedJob.Type);
            Assert.Equal(TestJobActorProvider.TenantId, persistedJob.TenantId);
            Assert.Equal(TestJobActorProvider.ActorId, persistedJob.CreatedByActorId);
            Assert.Equal(JobStatus.Scheduled, persistedJob.Status);
            Assert.Equal(scheduledAt, persistedJob.RunAt);
            Assert.Null(persistedJob.ClaimedBy);
            Assert.Null(persistedJob.ClaimedAt);
            Assert.Null(persistedJob.LeaseExpiresAt);
            Assert.Equal(persistedJob.History[^1].Id, persistedJob.CurrentStateChangeId);
            Assert.Equal(3, persistedJob.MaxAttempts);
            Assert.Null(persistedJob.FailureReason);
            Assert.Equal(
                """{"userId":"user_123","email":"person@example.com"}""",
                persistedJob.Payload.GetRawText());

            var history = persistedJob.History
                .OrderBy(change => change.Sequence)
                .ToArray();
            var stateChange = Assert.Single(history);
            Assert.Equal(JobStatus.Scheduled, stateChange.Status);
            Assert.Equal(1, stateChange.Sequence);
            Assert.Equal("Job scheduled.", stateChange.Reason);
            Assert.Equal(scheduledAt, stateChange.ScheduledAt);
            Assert.IsType<ScheduledJobStateDetails>(stateChange.Details);
        }
    }

    [Fact]
    public async Task MigrateAppliesMigrationsToFreshDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<JobSchedulerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var db = new JobSchedulerDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        await using (var db = new JobSchedulerDbContext(options))
        {
            Assert.True(await db.Database.CanConnectAsync());
            Assert.Contains(
                "20260505125233_InitialJobSchedulerSchema",
                await db.Database.GetAppliedMigrationsAsync());
            Assert.Contains(
                "20260505200107_AddJobClaimOwnership",
                await db.Database.GetAppliedMigrationsAsync());
            Assert.Contains(
                "20260505204411_AddJobLeaseExpiry",
                await db.Database.GetAppliedMigrationsAsync());
            Assert.Contains(
                "20260506094822_AddJobOwnership",
                await db.Database.GetAppliedMigrationsAsync());
        }
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse(
            """{"userId":"user_123","email":"person@example.com"}""");
        return document.RootElement.Clone();
    }

    private static JobSchedulerDbContext CreateDbContext(
        DbContextOptions<JobSchedulerDbContext> options,
        DataAccessScope dataAccessScope)
    {
        return new JobSchedulerDbContext(
            options,
            new FixedDataAccessScopeProvider(dataAccessScope));
    }

    private static JobRecord CreateQueuedJob(string tenantId)
    {
        return JobRecord.Enqueue(
            Guid.NewGuid(),
            tenantId,
            TestJobActorProvider.ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));
    }
}
