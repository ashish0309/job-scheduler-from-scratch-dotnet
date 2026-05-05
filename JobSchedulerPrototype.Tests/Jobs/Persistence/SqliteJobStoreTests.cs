using System.Collections.Concurrent;
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
        Assert.Equal(persistedJob.EnqueuedAt, persistedJob.RunAt);
        Assert.Null(persistedJob.ClaimedBy);
        Assert.Null(persistedJob.ClaimedAt);
        Assert.Null(persistedJob.LeaseExpiresAt);
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
        var initialStateChangeId = earlierJob.CurrentStateChangeId;
        var claimedAt = new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero);
        var leaseExpiresAt = claimedAt.AddMinutes(1);
        store.Add(laterJob);
        store.Add(earlierJob);

        var claimedJob = store.TryClaimNextDueJob(claimedAt, "worker-1", leaseExpiresAt);

        Assert.NotNull(claimedJob);
        Assert.Equal(earlierJob.Id, claimedJob.Id);
        Assert.Equal(JobStatus.Running, claimedJob.Status);
        Assert.Null(claimedJob.RunAt);
        Assert.Equal("worker-1", claimedJob.ClaimedBy);
        Assert.Equal(claimedAt, claimedJob.ClaimedAt);
        Assert.Equal(leaseExpiresAt, claimedJob.LeaseExpiresAt);
        Assert.Equal([JobStatus.Queued, JobStatus.Running], claimedJob.History.Select(change => change.Status));
        Assert.Equal(initialStateChangeId, claimedJob.History[0].Id);
        Assert.Equal(claimedJob.CurrentStateChangeId, claimedJob.History[^1].Id);
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
        var initialStateChangeId = job.CurrentStateChangeId;
        store.Add(job);

        var earlyClaim = store.TryClaimNextDueJob(scheduledAt.AddTicks(-1), "worker-1", (scheduledAt.AddTicks(-1)).AddMinutes(1));
        var dueClaim = store.TryClaimNextDueJob(scheduledAt, "worker-1", (scheduledAt).AddMinutes(1));

        Assert.Null(earlyClaim);
        Assert.NotNull(dueClaim);
        Assert.Equal(job.Id, dueClaim.Id);
        Assert.Equal(JobStatus.Running, dueClaim.Status);
        Assert.Null(dueClaim.RunAt);
        Assert.Equal("worker-1", dueClaim.ClaimedBy);
        Assert.Equal(scheduledAt, dueClaim.ClaimedAt);
        Assert.Equal(scheduledAt.AddMinutes(1), dueClaim.LeaseExpiresAt);
        Assert.Equal(
            [JobStatus.Scheduled, JobStatus.Queued, JobStatus.Running],
            dueClaim.History.Select(change => change.Status));
        Assert.Equal(initialStateChangeId, dueClaim.History[0].Id);
        Assert.Equal(dueClaim.CurrentStateChangeId, dueClaim.History[^1].Id);
        Assert.Equal(scheduledAt, dueClaim.ScheduledAt);
    }

    [Fact]
    public async Task TryClaimNextDueJobIgnoresTerminalJobEvenWhenRunAtIsPresent()
    {
        await using var database = await SqliteJobStoreDatabase.CreateAsync();
        var store = database.CreateStore();
        var now = new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero);
        var completedJob = CreateJob(enqueuedAt: now.AddMinutes(-1));
        var queuedJob = CreateJob(enqueuedAt: now);
        store.Add(completedJob);
        store.Add(queuedJob);
        var completedRunningJob = store.TryClaimNextDueJob(now, "worker-1", (now).AddMinutes(1));
        Assert.NotNull(completedRunningJob);
        store.MarkCompleted(completedJob.Id, completedRunningJob.CurrentStateChangeId);
        await database.ExecuteAsync(db =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Jobs" SET "RunAt" = {now.AddMinutes(-2).UtcDateTime.Ticks} WHERE "Id" = {completedJob.Id};"""));

        var claimedJob = store.TryClaimNextDueJob(now, "worker-1", (now).AddMinutes(1));

        Assert.NotNull(claimedJob);
        Assert.Equal(queuedJob.Id, claimedJob.Id);
        Assert.Equal(JobStatus.Running, claimedJob.Status);
    }

    [Fact]
    public async Task TryClaimNextDueJobReclaimsExpiredRunningJob()
    {
        await using var database = await SqliteJobStoreDatabase.CreateAsync();
        var store = database.CreateStore();
        var job = CreateJob();
        var claimedAt = new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero);
        var leaseExpiresAt = claimedAt.AddMinutes(1);
        var reclaimedAt = leaseExpiresAt;
        var newLeaseExpiresAt = reclaimedAt.AddMinutes(1);
        store.Add(job);
        store.TryClaimNextDueJob(claimedAt, "worker-1", leaseExpiresAt);

        var reclaimedJob = store.TryClaimNextDueJob(reclaimedAt, "worker-2", newLeaseExpiresAt);

        Assert.NotNull(reclaimedJob);
        Assert.Equal(job.Id, reclaimedJob.Id);
        Assert.Equal(JobStatus.Running, reclaimedJob.Status);
        Assert.Equal("worker-2", reclaimedJob.ClaimedBy);
        Assert.Equal(reclaimedAt, reclaimedJob.ClaimedAt);
        Assert.Equal(newLeaseExpiresAt, reclaimedJob.LeaseExpiresAt);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Running],
            reclaimedJob.History.Select(change => change.Status));
        Assert.Equal("Worker worker-2 reclaimed expired lease.", reclaimedJob.History[^1].Reason);
    }

    [Fact]
    public async Task TryClaimNextDueJobDoesNotReclaimRunningJobBeforeLeaseExpires()
    {
        await using var database = await SqliteJobStoreDatabase.CreateAsync();
        var store = database.CreateStore();
        var job = CreateJob();
        var claimedAt = new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero);
        var leaseExpiresAt = claimedAt.AddMinutes(1);
        store.Add(job);
        store.TryClaimNextDueJob(claimedAt, "worker-1", leaseExpiresAt);

        var reclaimedJob = store.TryClaimNextDueJob(
            leaseExpiresAt.AddTicks(-1),
            "worker-2",
            leaseExpiresAt.AddMinutes(1));

        Assert.Null(reclaimedJob);
        var persistedJob = database.CreateStore().Get(job.Id);
        Assert.NotNull(persistedJob);
        Assert.Equal("worker-1", persistedJob.ClaimedBy);
        Assert.Equal(leaseExpiresAt, persistedJob.LeaseExpiresAt);
    }

    [Fact]
    public async Task MarkCompletedCompletesRunningJob()
    {
        await using var database = await SqliteJobStoreDatabase.CreateAsync();
        var store = database.CreateStore();
        var job = CreateJob();
        store.Add(job);
        var runningJob = store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero), "worker-1", (new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero)).AddMinutes(1));
        Assert.NotNull(runningJob);

        var completed = store.MarkCompleted(job.Id, runningJob.CurrentStateChangeId);
        var persistedJob = database.CreateStore().Get(job.Id);

        Assert.True(completed);
        Assert.NotNull(persistedJob);
        Assert.Equal(JobStatus.Completed, persistedJob.Status);
        Assert.Null(persistedJob.RunAt);
        Assert.Null(persistedJob.ClaimedBy);
        Assert.Null(persistedJob.ClaimedAt);
        Assert.Null(persistedJob.LeaseExpiresAt);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Completed],
            persistedJob.History.Select(change => change.Status));
    }

    [Fact]
    public async Task MarkCompletedReturnsFalseWhenClaimTokenIsStale()
    {
        await using var database = await SqliteJobStoreDatabase.CreateAsync();
        var store = database.CreateStore();
        var job = CreateJob();
        var claimedAt = new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero);
        var leaseExpiresAt = claimedAt.AddMinutes(1);
        store.Add(job);
        var staleRunningJob = store.TryClaimNextDueJob(claimedAt, "worker-1", leaseExpiresAt);
        Assert.NotNull(staleRunningJob);
        var currentRunningJob = store.TryClaimNextDueJob(leaseExpiresAt, "worker-2", leaseExpiresAt.AddMinutes(1));
        Assert.NotNull(currentRunningJob);

        var completed = store.MarkCompleted(job.Id, staleRunningJob.CurrentStateChangeId);

        Assert.False(completed);
        var persistedJob = database.CreateStore().Get(job.Id);
        Assert.NotNull(persistedJob);
        Assert.Equal(JobStatus.Running, persistedJob.Status);
        Assert.Equal("worker-2", persistedJob.ClaimedBy);
        Assert.Equal(currentRunningJob.CurrentStateChangeId, persistedJob.CurrentStateChangeId);
    }

    [Fact]
    public async Task MarkFailedCompletesRunningJobAsFailed()
    {
        await using var database = await SqliteJobStoreDatabase.CreateAsync();
        var store = database.CreateStore();
        var job = CreateJob();
        store.Add(job);
        var runningJob = store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero), "worker-1", (new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero)).AddMinutes(1));
        Assert.NotNull(runningJob);

        var failed = store.MarkFailed(job.Id, runningJob.CurrentStateChangeId, "SMTP server unavailable.");
        var persistedJob = database.CreateStore().Get(job.Id);

        Assert.True(failed);
        Assert.NotNull(persistedJob);
        Assert.Equal(JobStatus.Failed, persistedJob.Status);
        Assert.Null(persistedJob.RunAt);
        Assert.Null(persistedJob.ClaimedBy);
        Assert.Null(persistedJob.ClaimedAt);
        Assert.Null(persistedJob.LeaseExpiresAt);
        Assert.Equal("SMTP server unavailable.", persistedJob.FailureReason);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Failed],
            persistedJob.History.Select(change => change.Status));
    }

    [Fact]
    public async Task MarkFailedReturnsFalseWhenClaimTokenIsStale()
    {
        await using var database = await SqliteJobStoreDatabase.CreateAsync();
        var store = database.CreateStore();
        var job = CreateJob();
        var claimedAt = new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero);
        var leaseExpiresAt = claimedAt.AddMinutes(1);
        store.Add(job);
        var staleRunningJob = store.TryClaimNextDueJob(claimedAt, "worker-1", leaseExpiresAt);
        Assert.NotNull(staleRunningJob);
        var currentRunningJob = store.TryClaimNextDueJob(leaseExpiresAt, "worker-2", leaseExpiresAt.AddMinutes(1));
        Assert.NotNull(currentRunningJob);

        var failed = store.MarkFailed(
            job.Id,
            staleRunningJob.CurrentStateChangeId,
            "SMTP server unavailable.");

        Assert.False(failed);
        var persistedJob = database.CreateStore().Get(job.Id);
        Assert.NotNull(persistedJob);
        Assert.Equal(JobStatus.Running, persistedJob.Status);
        Assert.Equal("worker-2", persistedJob.ClaimedBy);
        Assert.Equal(currentRunningJob.CurrentStateChangeId, persistedJob.CurrentStateChangeId);
    }

    [Fact]
    public async Task ScheduleRetryPersistsRetryAndMakesItClaimableWhenDue()
    {
        await using var database = await SqliteJobStoreDatabase.CreateAsync();
        var store = database.CreateStore();
        var job = CreateJob();
        var scheduledAt = new DateTimeOffset(2026, 5, 4, 10, 6, 0, TimeSpan.Zero);
        store.Add(job);
        var runningJob = store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero), "worker-1", (new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero)).AddMinutes(1));
        Assert.NotNull(runningJob);

        var scheduled = store.ScheduleRetry(job.Id, runningJob.CurrentStateChangeId, "SMTP server unavailable.", scheduledAt);
        var earlyClaim = store.TryClaimNextDueJob(scheduledAt.AddTicks(-1), "worker-1", (scheduledAt.AddTicks(-1)).AddMinutes(1));
        var claimedRetry = store.TryClaimNextDueJob(scheduledAt, "worker-1", (scheduledAt).AddMinutes(1));

        Assert.True(scheduled);
        Assert.Null(earlyClaim);
        Assert.NotNull(claimedRetry);
        Assert.Equal(job.Id, claimedRetry.Id);
        Assert.Equal(JobStatus.Running, claimedRetry.Status);
        Assert.Null(claimedRetry.RunAt);
        Assert.Equal("worker-1", claimedRetry.ClaimedBy);
        Assert.Equal(scheduledAt, claimedRetry.ClaimedAt);
        Assert.Equal(scheduledAt.AddMinutes(1), claimedRetry.LeaseExpiresAt);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Failed, JobStatus.Scheduled, JobStatus.Queued, JobStatus.Running],
            claimedRetry.History.Select(change => change.Status));
    }

    [Fact]
    public async Task ScheduleRetryReturnsFalseWhenClaimTokenIsStale()
    {
        await using var database = await SqliteJobStoreDatabase.CreateAsync();
        var store = database.CreateStore();
        var job = CreateJob();
        var claimedAt = new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero);
        var leaseExpiresAt = claimedAt.AddMinutes(1);
        var scheduledAt = leaseExpiresAt.AddMinutes(1);
        store.Add(job);
        var staleRunningJob = store.TryClaimNextDueJob(claimedAt, "worker-1", leaseExpiresAt);
        Assert.NotNull(staleRunningJob);
        var currentRunningJob = store.TryClaimNextDueJob(leaseExpiresAt, "worker-2", leaseExpiresAt.AddMinutes(1));
        Assert.NotNull(currentRunningJob);

        var scheduled = store.ScheduleRetry(
            job.Id,
            staleRunningJob.CurrentStateChangeId,
            "SMTP server unavailable.",
            scheduledAt);

        Assert.False(scheduled);
        var persistedJob = database.CreateStore().Get(job.Id);
        Assert.NotNull(persistedJob);
        Assert.Equal(JobStatus.Running, persistedJob.Status);
        Assert.Equal("worker-2", persistedJob.ClaimedBy);
        Assert.Equal(currentRunningJob.CurrentStateChangeId, persistedJob.CurrentStateChangeId);
    }

    [Fact]
    public async Task TryClaimNextDueJobClaimsEachDueJobOnceAcrossConcurrentWorkers()
    {
        await using var database = await SqliteJobStoreDatabase.CreateFileBackedAsync();
        var now = new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero);
        var jobs = Enumerable
            .Range(0, 25)
            .Select(index => CreateJob(enqueuedAt: now.AddSeconds(-index - 1)))
            .ToArray();

        foreach (var job in jobs)
        {
            database.CreateStore().Add(job);
        }

        var claimedJobIds = new ConcurrentBag<Guid>();
        var workers = Enumerable
            .Range(0, 8)
            .Select(_ => Task.Run(() =>
            {
                while (true)
                {
                    var claimedJob = database.CreateStore().TryClaimNextDueJob(now, "worker-1", (now).AddMinutes(1));
                    if (claimedJob is null)
                    {
                        return;
                    }

                    claimedJobIds.Add(claimedJob.Id);
                }
            }));

        await Task.WhenAll(workers);

        Assert.Equal(jobs.Length, claimedJobIds.Count);
        Assert.Equal(jobs.Length, claimedJobIds.Distinct().Count());
        Assert.Equal(
            jobs.Select(job => job.Id).Order(),
            claimedJobIds.Order());

        foreach (var job in jobs)
        {
            Assert.Equal(JobStatus.Running, database.CreateStore().Get(job.Id)?.Status);
        }
    }

    [Fact]
    public async Task TryClaimNextDueJobReclaimsExpiredRunningJobOnceAcrossConcurrentWorkers()
    {
        await using var database = await SqliteJobStoreDatabase.CreateFileBackedAsync();
        var job = CreateJob();
        var claimedAt = new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero);
        var leaseExpiresAt = claimedAt.AddMinutes(1);
        var reclaimedAt = leaseExpiresAt;
        database.CreateStore().Add(job);
        database.CreateStore().TryClaimNextDueJob(claimedAt, "worker-1", leaseExpiresAt);

        var claimedJobs = new ConcurrentBag<JobRecord>();
        var workers = Enumerable
            .Range(0, 8)
            .Select(index => Task.Run(() =>
            {
                var claimedJob = database.CreateStore().TryClaimNextDueJob(
                    reclaimedAt,
                    $"worker-{index + 2}",
                    reclaimedAt.AddMinutes(1));

                if (claimedJob is not null)
                {
                    claimedJobs.Add(claimedJob);
                }
            }));

        await Task.WhenAll(workers);

        var reclaimedJob = Assert.Single(claimedJobs);
        Assert.Equal(job.Id, reclaimedJob.Id);
        Assert.StartsWith("worker-", reclaimedJob.ClaimedBy);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Running],
            database.CreateStore().Get(job.Id)?.History.Select(change => change.Status));
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
        private readonly string? _databasePath;

        private SqliteJobStoreDatabase(
            SqliteConnection connection,
            DbContextOptions<JobSchedulerDbContext> options,
            string? databasePath = null)
        {
            _connection = connection;
            _options = options;
            _databasePath = databasePath;
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

        public static async Task<SqliteJobStoreDatabase> CreateFileBackedAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"job-scheduler-tests-{Guid.NewGuid():N}.db");
            var connectionString = $"Data Source={databasePath}";
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<JobSchedulerDbContext>()
                .UseSqlite(connectionString)
                .Options;

            await using (var db = new JobSchedulerDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
            }

            return new SqliteJobStoreDatabase(connection, options, databasePath);
        }

        public SqliteJobStore CreateStore()
        {
            return new SqliteJobStore(new TestDbContextFactory(_options));
        }

        public async Task ExecuteAsync(Func<JobSchedulerDbContext, Task> action)
        {
            await using var db = new JobSchedulerDbContext(_options);
            await action(db);
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();

            if (_databasePath is not null && File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
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
