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
        Assert.False(jobEntity.FindProperty(nameof(JobRecord.Type))?.IsNullable);
        Assert.Equal(
            200,
            jobEntity.FindProperty(nameof(JobRecord.Type))?.GetMaxLength());
        Assert.False(jobEntity.FindProperty(nameof(JobRecord.Payload))?.IsNullable);
        Assert.False(jobEntity.FindProperty(nameof(JobRecord.Status))?.IsNullable);
        Assert.Equal(
            50,
            jobEntity.FindProperty(nameof(JobRecord.Status))?.GetMaxLength());
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
            Assert.Equal(JobStatus.Scheduled, persistedJob.Status);
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

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse(
            """{"userId":"user_123","email":"person@example.com"}""");
        return document.RootElement.Clone();
    }
}
