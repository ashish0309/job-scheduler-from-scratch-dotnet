using System.Text.Json;
using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class JobRecordTests
{
    [Fact]
    public void EnqueueCreatesQueuedJobWithInitialHistory()
    {
        var id = Guid.NewGuid();
        var enqueuedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);

        var job = JobRecord.Enqueue(
            id,
            "send-welcome-email",
            Payload(),
            enqueuedAt);

        Assert.Equal(id, job.Id);
        Assert.Equal("send-welcome-email", job.Type);
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal(enqueuedAt, job.EnqueuedAt);
        Assert.Null(job.StartedAt);
        Assert.Null(job.CompletedAt);
        Assert.Null(job.FailedAt);

        var stateChange = Assert.Single(job.History);
        Assert.Equal(JobStatus.Queued, stateChange.Status);
        Assert.Equal(enqueuedAt, stateChange.ChangedAt);
    }

    [Fact]
    public void TransitionToAppendsHistoryAndUpdatesCurrentStatus()
    {
        var enqueuedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);
        var runningAt = enqueuedAt.AddSeconds(5);
        var completedAt = runningAt.AddSeconds(2);
        var job = JobRecord.Enqueue(
            Guid.NewGuid(),
            "send-welcome-email",
            Payload(),
            enqueuedAt);

        var runningJob = job.TransitionTo(JobStatus.Running, runningAt);
        var completedJob = runningJob.TransitionTo(JobStatus.Completed, completedAt);

        Assert.Equal(JobStatus.Completed, completedJob.Status);
        Assert.Equal(enqueuedAt, completedJob.EnqueuedAt);
        Assert.Equal(runningAt, completedJob.StartedAt);
        Assert.Equal(completedAt, completedJob.CompletedAt);
        Assert.Null(completedJob.FailedAt);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Completed],
            completedJob.History.Select(change => change.Status));
        Assert.Equal(completedAt, completedJob.History[^1].ChangedAt);
    }

    [Fact]
    public void TransitionToFailedAppendsHistoryAndCapturesReason()
    {
        var enqueuedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);
        var runningAt = enqueuedAt.AddSeconds(5);
        var failedAt = runningAt.AddSeconds(2);
        var job = JobRecord.Enqueue(
            Guid.NewGuid(),
            "send-welcome-email",
            Payload(),
            enqueuedAt);

        var runningJob = job.TransitionTo(JobStatus.Running, runningAt);
        var failedJob = runningJob.TransitionToFailed("SMTP server unavailable.", failedAt);

        Assert.Equal(JobStatus.Failed, failedJob.Status);
        Assert.Equal("SMTP server unavailable.", failedJob.FailureReason);
        Assert.Equal(runningAt, failedJob.StartedAt);
        Assert.Null(failedJob.CompletedAt);
        Assert.Equal(failedAt, failedJob.FailedAt);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Failed],
            failedJob.History.Select(change => change.Status));
        Assert.Equal(failedAt, failedJob.History[^1].ChangedAt);
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse("""{"userId":"user_123"}""");
        return document.RootElement.Clone();
    }
}
