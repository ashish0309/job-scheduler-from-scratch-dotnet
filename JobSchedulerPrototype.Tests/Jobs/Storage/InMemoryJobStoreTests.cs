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
        var claimedAt = new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero);
        var leaseExpiresAt = claimedAt.AddMinutes(1);
        store.Add(laterJob);
        store.Add(earlierJob);

        var claimedJob = store.TryClaimNextDueJob(claimedAt, "worker-1", leaseExpiresAt);

        Assert.NotNull(claimedJob);
        Assert.Equal(earlierJob.Id, claimedJob.Id);
        Assert.Equal(JobStatus.Running, claimedJob.Status);
        Assert.Equal("worker-1", claimedJob.ClaimedBy);
        Assert.Equal(claimedAt, claimedJob.ClaimedAt);
        Assert.Equal(leaseExpiresAt, claimedJob.LeaseExpiresAt);
        Assert.Equal(claimedJob.History[^1].Id, claimedJob.CurrentStateChangeId);
        Assert.Equal([JobStatus.Queued, JobStatus.Running], claimedJob.History.Select(change => change.Status));
        Assert.Equal("Worker worker-1 claimed job.", claimedJob.History[^1].Reason);
        Assert.Equal(JobStatus.Running, store.Get(earlierJob.Id)?.Status);
        Assert.Equal(JobStatus.Queued, store.Get(laterJob.Id)?.Status);
    }

    [Fact]
    public void TryClaimNextDueJobDoesNotClaimRunningJobAgain()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        store.Add(job);

        var firstClaim = store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero), "worker-1", (new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero)).AddMinutes(1));
        var secondClaim = store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero), "worker-1", (new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero)).AddMinutes(1));

        Assert.NotNull(firstClaim);
        Assert.Null(secondClaim);
    }

    [Fact]
    public void TryClaimNextDueJobReclaimsExpiredRunningJob()
    {
        var store = new InMemoryJobStore();
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
    public void TryClaimNextDueJobDoesNotClaimScheduledJobBeforeRunAt()
    {
        var store = new InMemoryJobStore();
        var scheduledAt = new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero);
        var job = CreateScheduledJob(scheduledAt);
        store.Add(job);

        var claimedJob = store.TryClaimNextDueJob(scheduledAt.AddTicks(-1), "worker-1", (scheduledAt.AddTicks(-1)).AddMinutes(1));

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

        var claimedJob = store.TryClaimNextDueJob(scheduledAt, "worker-1", (scheduledAt).AddMinutes(1));

        Assert.NotNull(claimedJob);
        Assert.Equal(job.Id, claimedJob.Id);
        Assert.Equal(JobStatus.Running, claimedJob.Status);
        Assert.Equal("worker-1", claimedJob.ClaimedBy);
        Assert.Equal(scheduledAt, claimedJob.ClaimedAt);
        Assert.Equal(scheduledAt.AddMinutes(1), claimedJob.LeaseExpiresAt);
        Assert.Equal(scheduledAt, claimedJob.ScheduledAt);
        Assert.Equal(
            [JobStatus.Scheduled, JobStatus.Queued, JobStatus.Running],
            claimedJob.History.Select(change => change.Status));
        Assert.Equal("Job queued.", claimedJob.History[^2].Reason);
        Assert.Equal("Worker worker-1 claimed job.", claimedJob.History[^1].Reason);
    }

    [Fact]
    public void MarkCompletedOnlyCompletesRunningJobs()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        store.Add(job);

        var queuedCompletion = store.MarkCompleted(job.Id);
        var runningJob = store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero), "worker-1", (new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero)).AddMinutes(1));
        var runningCompletion = store.MarkCompleted(job.Id);
        var completedJob = store.Get(job.Id);

        Assert.False(queuedCompletion);
        Assert.NotNull(runningJob);
        Assert.True(runningCompletion);
        Assert.NotNull(completedJob);
        Assert.Equal(JobStatus.Completed, completedJob.Status);
        Assert.Null(completedJob.ClaimedBy);
        Assert.Null(completedJob.ClaimedAt);
        Assert.Null(completedJob.LeaseExpiresAt);
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
        var runningJob = store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero), "worker-1", (new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero)).AddMinutes(1));
        var runningFailure = store.MarkFailed(job.Id, "SMTP server unavailable.");
        var failedJob = store.Get(job.Id);

        Assert.False(queuedFailure);
        Assert.NotNull(runningJob);
        Assert.True(runningFailure);
        Assert.NotNull(failedJob);
        Assert.Equal(JobStatus.Failed, failedJob.Status);
        Assert.Null(failedJob.ClaimedBy);
        Assert.Null(failedJob.ClaimedAt);
        Assert.Null(failedJob.LeaseExpiresAt);
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
        store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero), "worker-1", (new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero)).AddMinutes(1));

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
    public void ScheduleRetryMovesRunningJobToScheduledWhenAttemptsRemain()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        var scheduledAt = new DateTimeOffset(2026, 5, 4, 10, 6, 0, TimeSpan.Zero);
        store.Add(job);
        store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero), "worker-1", (new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero)).AddMinutes(1));

        var scheduled = store.ScheduleRetry(job.Id, "SMTP server unavailable.", scheduledAt);
        var retriedJob = store.Get(job.Id);

        Assert.True(scheduled);
        Assert.NotNull(retriedJob);
        Assert.Equal(JobStatus.Scheduled, retriedJob.Status);
        Assert.Null(retriedJob.ClaimedBy);
        Assert.Null(retriedJob.ClaimedAt);
        Assert.Null(retriedJob.LeaseExpiresAt);
        Assert.Equal(scheduledAt, retriedJob.ScheduledAt);
        Assert.Equal("SMTP server unavailable.", retriedJob.FailureReason);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Failed, JobStatus.Scheduled],
            retriedJob.History.Select(change => change.Status));

        Assert.Null(store.TryClaimNextDueJob(scheduledAt.AddTicks(-1), "worker-1", (scheduledAt.AddTicks(-1)).AddMinutes(1)));
        var claimedRetry = store.TryClaimNextDueJob(scheduledAt, "worker-1", (scheduledAt).AddMinutes(1));

        Assert.NotNull(claimedRetry);
        Assert.Equal(job.Id, claimedRetry.Id);
        Assert.Equal(JobStatus.Running, claimedRetry.Status);
        Assert.Equal(scheduledAt.AddMinutes(1), claimedRetry.LeaseExpiresAt);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Failed, JobStatus.Scheduled, JobStatus.Queued, JobStatus.Running],
            claimedRetry.History.Select(change => change.Status));
    }

    [Fact]
    public void ScheduleRetryReturnsFalseWhenJobIsNotEligible()
    {
        var store = new InMemoryJobStore();
        var exhaustedJob = CreateJob(
            enqueuedAt: new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero),
            maxAttempts: 1);
        var queuedJob = CreateJob(
            enqueuedAt: new DateTimeOffset(2026, 5, 4, 10, 1, 0, TimeSpan.Zero));
        var scheduledAt = new DateTimeOffset(2026, 5, 4, 10, 6, 0, TimeSpan.Zero);
        store.Add(queuedJob);
        store.Add(exhaustedJob);
        store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero), "worker-1", (new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero)).AddMinutes(1));

        var queuedRetry = store.ScheduleRetry(queuedJob.Id, "SMTP server unavailable.", scheduledAt);
        var exhaustedRetry = store.ScheduleRetry(exhaustedJob.Id, "SMTP server unavailable.", scheduledAt);
        var blankReasonRetry = store.ScheduleRetry(exhaustedJob.Id, "", scheduledAt);
        var missingRetry = store.ScheduleRetry(Guid.NewGuid(), "SMTP server unavailable.", scheduledAt);

        Assert.False(queuedRetry);
        Assert.False(exhaustedRetry);
        Assert.False(blankReasonRetry);
        Assert.False(missingRetry);
        Assert.Equal(JobStatus.Running, store.Get(exhaustedJob.Id)?.Status);
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
