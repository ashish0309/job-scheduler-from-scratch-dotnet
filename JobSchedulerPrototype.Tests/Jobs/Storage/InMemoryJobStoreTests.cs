using System.Text.Json;
using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class InMemoryJobStoreTests
{
    [Fact]
    public void ListReturnsJobsOrderedByEnqueueTime()
    {
        var store = new InMemoryJobStore();
        var earlierJob = CreateJob(enqueuedAt: new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));
        var laterJob = CreateJob(enqueuedAt: new DateTimeOffset(2026, 5, 4, 10, 1, 0, TimeSpan.Zero));

        store.Add(laterJob);
        store.Add(earlierJob);

        var jobs = store.List().ToArray();

        Assert.Equal([earlierJob.Id, laterJob.Id], jobs.Select(job => job.Id));
    }

    [Fact]
    public void TryClaimNextDueJobClaimsOldestQueuedJob()
    {
        var store = new InMemoryJobStore();
        var earlierJob = CreateJob(enqueuedAt: new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));
        var laterJob = CreateJob(enqueuedAt: new DateTimeOffset(2026, 5, 4, 10, 1, 0, TimeSpan.Zero));
        store.Add(laterJob);
        store.Add(earlierJob);

        var claimedJob = store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero));

        Assert.NotNull(claimedJob);
        Assert.Equal(earlierJob.Id, claimedJob.Id);
        Assert.Equal(JobStatus.Running, claimedJob.Status);
        Assert.Equal(claimedJob.History[^1].Id, claimedJob.CurrentStateChangeId);
        Assert.Equal([JobStatus.Queued, JobStatus.Running], claimedJob.History.Select(change => change.Status));
        Assert.Equal("Worker claimed job.", claimedJob.History[^1].Reason);
        Assert.Equal(JobStatus.Running, store.Get(earlierJob.Id)?.Status);
        Assert.Equal(JobStatus.Queued, store.Get(laterJob.Id)?.Status);
    }

    [Fact]
    public void TryClaimNextDueJobDoesNotClaimRunningJobAgain()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        store.Add(job);

        var firstClaim = store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero));
        var secondClaim = store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero));

        Assert.NotNull(firstClaim);
        Assert.Null(secondClaim);
    }

    [Fact]
    public void TryClaimNextDueJobDoesNotClaimScheduledJobBeforeRunAt()
    {
        var store = new InMemoryJobStore();
        var scheduledAt = new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero);
        var job = CreateScheduledJob(scheduledAt);
        store.Add(job);

        var claimedJob = store.TryClaimNextDueJob(scheduledAt.AddTicks(-1));

        Assert.Null(claimedJob);
        Assert.Equal(JobStatus.Scheduled, store.Get(job.Id)?.Status);
    }

    [Fact]
    public void TryClaimNextDueJobClaimsScheduledJobWhenRunAtArrives()
    {
        var store = new InMemoryJobStore();
        var scheduledAt = new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero);
        var job = CreateScheduledJob(scheduledAt);
        store.Add(job);

        var claimedJob = store.TryClaimNextDueJob(scheduledAt);

        Assert.NotNull(claimedJob);
        Assert.Equal(job.Id, claimedJob.Id);
        Assert.Equal(JobStatus.Running, claimedJob.Status);
        Assert.Equal(scheduledAt, claimedJob.ScheduledAt);
        Assert.Equal(
            [JobStatus.Scheduled, JobStatus.Queued, JobStatus.Running],
            claimedJob.History.Select(change => change.Status));
        Assert.Equal("Job queued.", claimedJob.History[^2].Reason);
        Assert.Equal("Worker claimed job.", claimedJob.History[^1].Reason);
    }

    [Fact]
    public void MarkCompletedOnlyCompletesRunningJobs()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        store.Add(job);

        var queuedCompletion = store.MarkCompleted(job.Id);
        var runningJob = store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero));
        var runningCompletion = store.MarkCompleted(job.Id);
        var completedJob = store.Get(job.Id);

        Assert.False(queuedCompletion);
        Assert.NotNull(runningJob);
        Assert.True(runningCompletion);
        Assert.NotNull(completedJob);
        Assert.Equal(JobStatus.Completed, completedJob.Status);
        Assert.Equal(completedJob.History[^1].Id, completedJob.CurrentStateChangeId);
        Assert.Equal("Job completed successfully.", completedJob.History[^1].Reason);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Completed],
            completedJob.History.Select(change => change.Status));
    }

    [Fact]
    public void MarkCompletedReturnsFalseWhenJobDoesNotExist()
    {
        var store = new InMemoryJobStore();

        var completed = store.MarkCompleted(Guid.NewGuid());

        Assert.False(completed);
    }

    [Fact]
    public void MarkFailedOnlyFailsRunningJobs()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        store.Add(job);

        var queuedFailure = store.MarkFailed(job.Id, "SMTP server unavailable.");
        var runningJob = store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero));
        var runningFailure = store.MarkFailed(job.Id, "SMTP server unavailable.");
        var failedJob = store.Get(job.Id);

        Assert.False(queuedFailure);
        Assert.NotNull(runningJob);
        Assert.True(runningFailure);
        Assert.NotNull(failedJob);
        Assert.Equal(JobStatus.Failed, failedJob.Status);
        Assert.Equal(failedJob.History[^1].Id, failedJob.CurrentStateChangeId);
        Assert.Equal("SMTP server unavailable.", failedJob.FailureReason);
        Assert.Equal("SMTP server unavailable.", failedJob.History[^1].Reason);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Failed],
            failedJob.History.Select(change => change.Status));
    }

    [Fact]
    public void MarkFailedReturnsFalseWhenReasonIsBlank()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        store.Add(job);
        store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero));

        var failed = store.MarkFailed(job.Id, "");

        Assert.False(failed);
        Assert.Equal(JobStatus.Running, store.Get(job.Id)?.Status);
    }

    [Fact]
    public void MarkFailedReturnsFalseWhenJobDoesNotExist()
    {
        var store = new InMemoryJobStore();

        var failed = store.MarkFailed(Guid.NewGuid(), "SMTP server unavailable.");

        Assert.False(failed);
    }

    [Fact]
    public void RetryMovesFailedJobBackToQueuedWhenAttemptsRemain()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        store.Add(job);
        store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero));
        store.MarkFailed(job.Id, "SMTP server unavailable.");

        var retried = store.Retry(job.Id);
        var retriedJob = store.Get(job.Id);

        Assert.True(retried);
        Assert.NotNull(retriedJob);
        Assert.Equal(JobStatus.Queued, retriedJob.Status);
        Assert.Equal(retriedJob.History[^1].Id, retriedJob.CurrentStateChangeId);
        Assert.Null(retriedJob.FailureReason);
        Assert.Equal(1, retriedJob.AttemptCount);
        Assert.False(retriedJob.RetryAvailable);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Failed, JobStatus.Queued],
            retriedJob.History.Select(change => change.Status));
        Assert.Equal("Manually retried.", retriedJob.History[^1].Reason);
    }

    [Fact]
    public void RetryReturnsFalseWhenJobIsNotEligible()
    {
        var store = new InMemoryJobStore();
        var exhaustedJob = CreateJob(
            enqueuedAt: new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero),
            maxAttempts: 1);
        var queuedJob = CreateJob(
            enqueuedAt: new DateTimeOffset(2026, 5, 4, 10, 1, 0, TimeSpan.Zero));
        store.Add(queuedJob);
        store.Add(exhaustedJob);
        store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero));
        store.MarkFailed(exhaustedJob.Id, "SMTP server unavailable.");

        var queuedRetry = store.Retry(queuedJob.Id);
        var exhaustedRetry = store.Retry(exhaustedJob.Id);
        var missingRetry = store.Retry(Guid.NewGuid());

        Assert.False(queuedRetry);
        Assert.False(exhaustedRetry);
        Assert.False(missingRetry);
        Assert.Equal(JobStatus.Failed, store.Get(exhaustedJob.Id)?.Status);
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
}
