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
            maxAttempts: 3,
            enqueuedAt);

        Assert.Equal(id, job.Id);
        Assert.Equal("send-welcome-email", job.Type);
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal(3, job.MaxAttempts);
        Assert.Equal(0, job.AttemptCount);
        Assert.False(job.RetryAvailable);
        Assert.Equal(enqueuedAt, job.EnqueuedAt);
        Assert.Equal(job.History[^1].Id, job.CurrentStateChangeId);
        Assert.Null(job.StartedAt);
        Assert.Null(job.CompletedAt);
        Assert.Null(job.FailedAt);

        var stateChange = Assert.Single(job.History);
        Assert.NotEqual(Guid.Empty, stateChange.Id);
        Assert.Equal(JobStatus.Queued, stateChange.Status);
        Assert.Equal(enqueuedAt, stateChange.ChangedAt);
        Assert.Equal("Job accepted.", stateChange.Reason);
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
            maxAttempts: 3,
            enqueuedAt);

        var runningJob = job.TransitionTo(JobStatus.Running, runningAt);
        var completedJob = runningJob.TransitionTo(JobStatus.Completed, completedAt);

        Assert.Equal(JobStatus.Completed, completedJob.Status);
        Assert.Equal(completedJob.History[^1].Id, completedJob.CurrentStateChangeId);
        Assert.Equal(1, completedJob.AttemptCount);
        Assert.Equal(enqueuedAt, completedJob.EnqueuedAt);
        Assert.Equal(runningAt, completedJob.StartedAt);
        Assert.Equal(completedAt, completedJob.CompletedAt);
        Assert.Null(completedJob.FailedAt);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Completed],
            completedJob.History.Select(change => change.Status));
        Assert.Equal(completedAt, completedJob.History[^1].ChangedAt);
        Assert.Equal("Job completed successfully.", completedJob.History[^1].Reason);
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
            maxAttempts: 3,
            enqueuedAt);

        var runningJob = job.TransitionTo(JobStatus.Running, runningAt);
        var failedJob = runningJob.TransitionToFailed("SMTP server unavailable.", failedAt);

        Assert.Equal(JobStatus.Failed, failedJob.Status);
        Assert.Equal(failedJob.History[^1].Id, failedJob.CurrentStateChangeId);
        Assert.Equal("SMTP server unavailable.", failedJob.FailureReason);
        Assert.Equal(1, failedJob.AttemptCount);
        Assert.True(failedJob.RetryAvailable);
        Assert.Equal(runningAt, failedJob.StartedAt);
        Assert.Null(failedJob.CompletedAt);
        Assert.Equal(failedAt, failedJob.FailedAt);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Failed],
            failedJob.History.Select(change => change.Status));
        Assert.Equal(failedAt, failedJob.History[^1].ChangedAt);
        Assert.Equal("SMTP server unavailable.", failedJob.History[^1].Reason);
    }

    [Fact]
    public void RetryMovesFailedJobBackToQueuedAndClearsFailureReason()
    {
        var enqueuedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);
        var retryAt = enqueuedAt.AddMinutes(1);
        var job = JobRecord.Enqueue(
            Guid.NewGuid(),
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            enqueuedAt);
        var failedJob = job
            .TransitionTo(JobStatus.Running, enqueuedAt.AddSeconds(1))
            .TransitionToFailed("SMTP server unavailable.", enqueuedAt.AddSeconds(2));

        var retriedJob = failedJob.Retry(retryAt);

        Assert.Equal(JobStatus.Queued, retriedJob.Status);
        Assert.Equal(retriedJob.History[^1].Id, retriedJob.CurrentStateChangeId);
        Assert.Null(retriedJob.FailureReason);
        Assert.Equal(1, retriedJob.AttemptCount);
        Assert.False(retriedJob.RetryAvailable);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Failed, JobStatus.Queued],
            retriedJob.History.Select(change => change.Status));
        Assert.Equal(retryAt, retriedJob.History[^1].ChangedAt);
        Assert.Equal("Manually retried.", retriedJob.History[^1].Reason);
    }

    [Fact]
    public void EnqueueRejectsMaxAttemptsLessThanOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobRecord.Enqueue(
            Guid.NewGuid(),
            "send-welcome-email",
            Payload(),
            maxAttempts: 0,
            new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero)));
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse("""{"userId":"user_123"}""");
        return document.RootElement.Clone();
    }
}
