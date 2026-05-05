using System.Text.Json;
using JobSchedulerPrototype.Jobs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class SqliteJobStoreTests
{
    [Fact]
    public async Task AddPersistsJobAcrossStoreInstances()
    {
        await using var database = await SqliteJobStoreDatabase.CreateAsync();
        var job = CreateJob();

        database.CreateStore().Add(job);

        var persistedJob = database.CreateStore().Get(job.Id);

        Assert.NotNull(persistedJob);
        Assert.Equal(job.Id, persistedJob.Id);
        Assert.Equal(JobStatus.Queued, persistedJob.Status);
        Assert.Equal("""{"userId":"user_123"}""", persistedJob.Payload.GetRawText());
        var stateChange = Assert.Single(persistedJob.History);
        Assert.Equal(persistedJob.CurrentStateChangeId, stateChange.Id);
        Assert.Equal(JobStatus.Queued, stateChange.Status);
        Assert.Equal(1, stateChange.Sequence);
        Assert.Equal("Job accepted.", stateChange.Reason);
        Assert.Equal(persistedJob.EnqueuedAt, stateChange.ChangedAt);
    }

    [Fact]
    public async Task ListReturnsJobsOrderedByEnqueueTime()
    {
        await using var database = await SqliteJobStoreDatabase.CreateAsync();
        var store = database.CreateStore();
        var earlierJob = CreateJob(enqueuedAt: new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));
        var laterJob = CreateJob(enqueuedAt: new DateTimeOffset(2026, 5, 4, 10, 1, 0, TimeSpan.Zero));

        store.Add(laterJob);
        store.Add(earlierJob);

        var jobs = store.List().ToArray();

        Assert.Equal([earlierJob.Id, laterJob.Id], jobs.Select(job => job.Id));
    }

    [Fact]
    public async Task TryClaimNextDueJobClaimsOldestQueuedJob()
    {
        await using var database = await SqliteJobStoreDatabase.CreateAsync();
        var store = database.CreateStore();
        var earlierJob = CreateJob(enqueuedAt: new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));
        var laterJob = CreateJob(enqueuedAt: new DateTimeOffset(2026, 5, 4, 10, 1, 0, TimeSpan.Zero));
        store.Add(laterJob);
        store.Add(earlierJob);

        var claimedJob = store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero));

        Assert.NotNull(claimedJob);
        Assert.Equal(earlierJob.Id, claimedJob.Id);
        Assert.Equal(JobStatus.Running, claimedJob.Status);
        Assert.Equal([JobStatus.Queued, JobStatus.Running], claimedJob.History.Select(change => change.Status));
        Assert.Equal(JobStatus.Running, database.CreateStore().Get(earlierJob.Id)?.Status);
        Assert.Equal(JobStatus.Queued, database.CreateStore().Get(laterJob.Id)?.Status);
    }

    [Fact]
    public async Task TryClaimNextDueJobClaimsScheduledJobOnlyWhenRunAtArrives()
    {
        await using var database = await SqliteJobStoreDatabase.CreateAsync();
        var store = database.CreateStore();
        var scheduledAt = new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero);
        var job = CreateScheduledJob(scheduledAt);
        store.Add(job);

        var earlyClaim = store.TryClaimNextDueJob(scheduledAt.AddTicks(-1));
        var dueClaim = store.TryClaimNextDueJob(scheduledAt);

        Assert.Null(earlyClaim);
        Assert.NotNull(dueClaim);
        Assert.Equal(job.Id, dueClaim.Id);
        Assert.Equal(JobStatus.Running, dueClaim.Status);
        Assert.Equal(
            [JobStatus.Scheduled, JobStatus.Queued, JobStatus.Running],
            dueClaim.History.Select(change => change.Status));
        Assert.Equal(scheduledAt, dueClaim.ScheduledAt);
    }

    [Fact]
    public async Task MarkCompletedCompletesRunningJob()
    {
        await using var database = await SqliteJobStoreDatabase.CreateAsync();
        var store = database.CreateStore();
        var job = CreateJob();
        store.Add(job);
        store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero));

        var completed = store.MarkCompleted(job.Id);
        var persistedJob = database.CreateStore().Get(job.Id);

        Assert.True(completed);
        Assert.NotNull(persistedJob);
        Assert.Equal(JobStatus.Completed, persistedJob.Status);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Completed],
            persistedJob.History.Select(change => change.Status));
    }

    [Fact]
    public async Task MarkFailedCompletesRunningJobAsFailed()
    {
        await using var database = await SqliteJobStoreDatabase.CreateAsync();
        var store = database.CreateStore();
        var job = CreateJob();
        store.Add(job);
        store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero));

        var failed = store.MarkFailed(job.Id, "SMTP server unavailable.");
        var persistedJob = database.CreateStore().Get(job.Id);

        Assert.True(failed);
        Assert.NotNull(persistedJob);
        Assert.Equal(JobStatus.Failed, persistedJob.Status);
        Assert.Equal("SMTP server unavailable.", persistedJob.FailureReason);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Failed],
            persistedJob.History.Select(change => change.Status));
    }

    [Fact]
    public async Task ScheduleRetryPersistsRetryAndMakesItClaimableWhenDue()
    {
        await using var database = await SqliteJobStoreDatabase.CreateAsync();
        var store = database.CreateStore();
        var job = CreateJob();
        var scheduledAt = new DateTimeOffset(2026, 5, 4, 10, 6, 0, TimeSpan.Zero);
        store.Add(job);
        store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero));

        var scheduled = store.ScheduleRetry(job.Id, "SMTP server unavailable.", scheduledAt);
        var earlyClaim = store.TryClaimNextDueJob(scheduledAt.AddTicks(-1));
        var claimedRetry = store.TryClaimNextDueJob(scheduledAt);

        Assert.True(scheduled);
        Assert.Null(earlyClaim);
        Assert.NotNull(claimedRetry);
        Assert.Equal(job.Id, claimedRetry.Id);
        Assert.Equal(JobStatus.Running, claimedRetry.Status);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Failed, JobStatus.Scheduled, JobStatus.Queued, JobStatus.Running],
            claimedRetry.History.Select(change => change.Status));
    }

    private static JobRecord CreateJob(DateTimeOffset? enqueuedAt = null, int maxAttempts = 3)
    {
        return JobRecord.Enqueue(
            Guid.NewGuid(),
            "send-welcome-email",
            Payload(),
            maxAttempts,
            enqueuedAt ?? new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));
    }

    private static JobRecord CreateScheduledJob(DateTimeOffset scheduledAt, int maxAttempts = 3)
    {
        return JobRecord.Schedule(
            Guid.NewGuid(),
            "send-welcome-email",
            Payload(),
            maxAttempts,
            scheduledAt,
            new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse("""{"userId":"user_123"}""");
        return document.RootElement.Clone();
    }

    private sealed class SqliteJobStoreDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<JobSchedulerDbContext> _options;

        private SqliteJobStoreDatabase(
            SqliteConnection connection,
            DbContextOptions<JobSchedulerDbContext> options)
        {
            _connection = connection;
            _options = options;
        }

        public static async Task<SqliteJobStoreDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<JobSchedulerDbContext>()
                .UseSqlite(connection)
                .Options;

            await using (var db = new JobSchedulerDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
            }

            return new SqliteJobStoreDatabase(connection, options);
        }

        public SqliteJobStore CreateStore()
        {
            return new SqliteJobStore(new TestDbContextFactory(_options));
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
        }
    }

    private sealed class TestDbContextFactory : IDbContextFactory<JobSchedulerDbContext>
    {
        private readonly DbContextOptions<JobSchedulerDbContext> _options;

        public TestDbContextFactory(DbContextOptions<JobSchedulerDbContext> options)
        {
            _options = options;
        }

        public JobSchedulerDbContext CreateDbContext()
        {
            return new JobSchedulerDbContext(_options);
        }
    }
}
