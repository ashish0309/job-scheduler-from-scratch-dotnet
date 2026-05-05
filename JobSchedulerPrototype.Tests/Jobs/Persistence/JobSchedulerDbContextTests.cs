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

        var jobEntity = db.Model.FindEntityType(typeof(JobEntity));
        Assert.NotNull(jobEntity);
        Assert.False(jobEntity.FindProperty(nameof(JobEntity.Type))?.IsNullable);
        Assert.Equal(
            JobEntity.TypeMaxLength,
            jobEntity.FindProperty(nameof(JobEntity.Type))?.GetMaxLength());
        Assert.False(jobEntity.FindProperty(nameof(JobEntity.PayloadJson))?.IsNullable);
        Assert.False(jobEntity.FindProperty(nameof(JobEntity.Status))?.IsNullable);
        Assert.Equal(
            JobEntity.StatusMaxLength,
            jobEntity.FindProperty(nameof(JobEntity.Status))?.GetMaxLength());
        Assert.Equal(
            JobEntity.FailureReasonMaxLength,
            jobEntity.FindProperty(nameof(JobEntity.FailureReason))?.GetMaxLength());

        var stateChangeEntity = db.Model.FindEntityType(typeof(JobStateChangeEntity));
        Assert.NotNull(stateChangeEntity);
        Assert.False(stateChangeEntity.FindProperty(nameof(JobStateChangeEntity.Status))?.IsNullable);
        Assert.Equal(
            JobStateChangeEntity.StatusMaxLength,
            stateChangeEntity.FindProperty(nameof(JobStateChangeEntity.Status))?.GetMaxLength());
        Assert.False(stateChangeEntity.FindProperty(nameof(JobStateChangeEntity.Reason))?.IsNullable);
        Assert.Equal(
            JobStateChangeEntity.ReasonMaxLength,
            stateChangeEntity.FindProperty(nameof(JobStateChangeEntity.Reason))?.GetMaxLength());
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
        var queuedChangeId = Guid.NewGuid();
        var scheduledChangeId = Guid.NewGuid();
        var changedAt = new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero);
        var scheduledChangedAt = changedAt.AddSeconds(1);
        var scheduledAt = changedAt.AddSeconds(30);

        await using (var db = new JobSchedulerDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            var job = new JobEntity
            {
                Id = jobId,
                Type = "send-welcome-email",
                PayloadJson = """{"userId":"user_123","email":"person@example.com"}""",
                Status = JobStatus.Scheduled,
                CurrentStateChangeId = scheduledChangeId,
                MaxAttempts = 3,
                FailureReason = null
            };
            job.History.AddRange(
            [
                new JobStateChangeEntity
                {
                    Id = queuedChangeId,
                    Status = JobStatus.Queued,
                    ChangedAt = changedAt,
                    Reason = "Job accepted."
                },
                new JobStateChangeEntity
                {
                    Id = scheduledChangeId,
                    Status = JobStatus.Scheduled,
                    ChangedAt = scheduledChangedAt,
                    Reason = "Job scheduled.",
                    ScheduledAt = scheduledAt
                }
            ]);
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
        }

        await using (var db = new JobSchedulerDbContext(options))
        {
            var job = await db.Jobs
                .Include(entity => entity.History)
                .SingleAsync(entity => entity.Id == jobId);

            Assert.Equal("send-welcome-email", job.Type);
            Assert.Equal(JobStatus.Scheduled, job.Status);
            Assert.Equal(scheduledChangeId, job.CurrentStateChangeId);
            Assert.Equal(3, job.MaxAttempts);
            Assert.Null(job.FailureReason);
            Assert.Equal(
                """{"userId":"user_123","email":"person@example.com"}""",
                job.PayloadJson);

            var history = job.History
                .OrderBy(change => change.ChangedAt)
                .ThenBy(change => change.Id)
                .ToArray();
            Assert.Equal(2, history.Length);
            Assert.Equal(JobStatus.Queued, history[0].Status);
            Assert.Equal("Job accepted.", history[0].Reason);
            Assert.Null(history[0].ScheduledAt);
            Assert.Equal(JobStatus.Scheduled, history[1].Status);
            Assert.Equal("Job scheduled.", history[1].Reason);
            Assert.Equal(scheduledAt, history[1].ScheduledAt);
        }
    }
}
