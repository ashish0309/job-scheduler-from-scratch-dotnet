using System.Text.Json;
using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class JobRecordTests
{
    private const string TenantId = "tenant-alpha";
    private const string ActorId = "user-123";

    [Fact]
    public void EnqueueCreatesQueuedJobWithInitialHistory()
    {
        var id = Guid.NewGuid();
        var enqueuedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);

        var job = JobRecord.Enqueue(
            id,
            TenantId,
            ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            enqueuedAt);

        Assert.Equal(id, job.Id);
        Assert.Equal(TenantId, job.TenantId);
        Assert.Equal(ActorId, job.CreatedByActorId);
        Assert.Equal("send-welcome-email", job.Type);
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal(3, job.MaxAttempts);
        Assert.Equal(0, job.AttemptCount);
        Assert.Equal(enqueuedAt, job.EnqueuedAt);
        Assert.Equal(enqueuedAt, job.RunAt);
        Assert.Null(job.ClaimedBy);
        Assert.Null(job.ClaimedAt);
        Assert.Null(job.LeaseExpiresAt);
        Assert.Equal(job.History[^1].Id, job.CurrentStateChangeId);
        Assert.Null(job.StartedAt);
        Assert.Null(job.CompletedAt);
        Assert.Null(job.FailedAt);
        Assert.Null(job.ScheduledAt);

        var stateChange = Assert.Single(job.History);
        Assert.NotEqual(Guid.Empty, stateChange.Id);
        Assert.Equal(JobStatus.Queued, stateChange.Status);
        Assert.Equal(enqueuedAt, stateChange.ChangedAt);
        Assert.Equal("Job accepted.", stateChange.Reason);
        Assert.Same(JobStateDetails.None, stateChange.Details);
    }

    [Fact]
    public void ScheduleCreatesScheduledJobWithScheduledStateDetails()
    {
        var id = Guid.NewGuid();
        var changedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);
        var scheduledAt = changedAt.AddSeconds(30);

        var job = JobRecord.Schedule(
            id,
            TenantId,
            ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            scheduledAt,
            changedAt);

        Assert.Equal(id, job.Id);
        Assert.Equal(TenantId, job.TenantId);
        Assert.Equal(ActorId, job.CreatedByActorId);
        Assert.Equal(JobStatus.Scheduled, job.Status);
        Assert.Equal(scheduledAt, job.ScheduledAt);
        Assert.Equal(scheduledAt, job.RunAt);
        Assert.Null(job.ClaimedBy);
        Assert.Null(job.ClaimedAt);
        Assert.Null(job.LeaseExpiresAt);
        Assert.Equal(changedAt, job.EnqueuedAt);
        Assert.Equal(job.History[^1].Id, job.CurrentStateChangeId);

        var stateChange = Assert.Single(job.History);
        Assert.Equal(JobStatus.Scheduled, stateChange.Status);
        Assert.Equal(changedAt, stateChange.ChangedAt);
        Assert.Equal("Job scheduled.", stateChange.Reason);
        var details = Assert.IsType<ScheduledJobStateDetails>(stateChange.Details);
        Assert.Equal(scheduledAt, details.ScheduledAt);
        Assert.Equal(scheduledAt, stateChange.ScheduledAt);
    }

    [Fact]
    public void TransitionToAppendsHistoryAndUpdatesCurrentStatus()
    {
        var enqueuedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);
        var runningAt = enqueuedAt.AddSeconds(5);
        var completedAt = runningAt.AddSeconds(2);
        var job = JobRecord.Enqueue(
            Guid.NewGuid(),
            TenantId,
            ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            enqueuedAt);

        var runningJob = job.TransitionTo(JobStatus.Running, runningAt);
        var completedJob = runningJob.TransitionTo(JobStatus.Completed, completedAt);

        Assert.Equal(JobStatus.Completed, completedJob.Status);
        Assert.Null(completedJob.RunAt);
        Assert.Null(completedJob.ClaimedBy);
        Assert.Null(completedJob.ClaimedAt);
        Assert.Null(completedJob.LeaseExpiresAt);
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

        var attempt = Assert.Single(completedJob.Attempts);
        Assert.Equal(1, attempt.Number);
        Assert.Equal(JobStatus.Completed, attempt.Status);
        Assert.Equal(runningAt, attempt.StartedAt);
        Assert.Equal(completedAt, attempt.CompletedAt);
        Assert.Null(attempt.FailedAt);
        Assert.Equal(TimeSpan.FromSeconds(2), attempt.Duration);
        Assert.Null(attempt.FailureReason);
    }

    [Fact]
    public void ClaimAppendsRunningHistoryWithWorkerId()
    {
        var enqueuedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);
        var claimedAt = enqueuedAt.AddSeconds(5);
        var leaseExpiresAt = claimedAt.AddMinutes(1);
        var job = JobRecord.Enqueue(
            Guid.NewGuid(),
            TenantId,
            ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            enqueuedAt);

        var claimedJob = job.Claim("worker-2", claimedAt, leaseExpiresAt);

        Assert.Equal(JobStatus.Running, claimedJob.Status);
        Assert.Null(claimedJob.RunAt);
        Assert.Equal("worker-2", claimedJob.ClaimedBy);
        Assert.Equal(claimedAt, claimedJob.ClaimedAt);
        Assert.Equal(leaseExpiresAt, claimedJob.LeaseExpiresAt);
        Assert.Equal(claimedJob.History[^1].Id, claimedJob.CurrentStateChangeId);
        Assert.Equal("Worker worker-2 claimed job.", claimedJob.History[^1].Reason);
    }

    [Fact]
    public void ClaimRejectsLeaseExpiryAtOrBeforeClaimTime()
    {
        var claimedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 5, TimeSpan.Zero);
        var job = JobRecord.Enqueue(
            Guid.NewGuid(),
            TenantId,
            ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));

        Assert.Throws<ArgumentOutOfRangeException>(() => job.Claim("worker-1", claimedAt, claimedAt));
    }

    [Fact]
    public void ReclaimExpiredLeaseAppendsRunningHistoryWithNewWorkerId()
    {
        var enqueuedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);
        var firstClaimedAt = enqueuedAt.AddSeconds(5);
        var firstLeaseExpiresAt = firstClaimedAt.AddMinutes(1);
        var reclaimedAt = firstLeaseExpiresAt;
        var secondLeaseExpiresAt = reclaimedAt.AddMinutes(1);
        var job = JobRecord.Enqueue(
            Guid.NewGuid(),
            TenantId,
            ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            enqueuedAt)
            .Claim("worker-1", firstClaimedAt, firstLeaseExpiresAt);

        var reclaimedJob = job.ReclaimExpiredLease("worker-2", reclaimedAt, secondLeaseExpiresAt);

        Assert.Equal(JobStatus.Running, reclaimedJob.Status);
        Assert.Equal("worker-2", reclaimedJob.ClaimedBy);
        Assert.Equal(reclaimedAt, reclaimedJob.ClaimedAt);
        Assert.Equal(secondLeaseExpiresAt, reclaimedJob.LeaseExpiresAt);
        Assert.Equal(2, reclaimedJob.AttemptCount);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Running],
            reclaimedJob.History.Select(change => change.Status));
        Assert.Equal(reclaimedJob.History[^1].Id, reclaimedJob.CurrentStateChangeId);
        Assert.Equal("Worker worker-2 reclaimed expired lease.", reclaimedJob.History[^1].Reason);
    }

    [Fact]
    public void ReclaimExpiredLeaseRejectsActiveLease()
    {
        var enqueuedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);
        var claimedAt = enqueuedAt.AddSeconds(5);
        var leaseExpiresAt = claimedAt.AddMinutes(1);
        var job = JobRecord.Enqueue(
            Guid.NewGuid(),
            TenantId,
            ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            enqueuedAt)
            .Claim("worker-1", claimedAt, leaseExpiresAt);

        Assert.Throws<InvalidOperationException>(() => job.ReclaimExpiredLease(
            "worker-2",
            leaseExpiresAt.AddTicks(-1),
            leaseExpiresAt.AddMinutes(1)));
    }

    [Fact]
    public void RenewLeaseExtendsLeaseWithoutAddingHistory()
    {
        var enqueuedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);
        var claimedAt = enqueuedAt.AddSeconds(5);
        var leaseExpiresAt = claimedAt.AddMinutes(1);
        var renewedAt = claimedAt.AddSeconds(30);
        var renewedLeaseExpiresAt = renewedAt.AddMinutes(1);
        var job = JobRecord.Enqueue(
            Guid.NewGuid(),
            TenantId,
            ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            enqueuedAt)
            .Claim("worker-1", claimedAt, leaseExpiresAt);

        var renewedJob = job.RenewLease(renewedAt, renewedLeaseExpiresAt);

        Assert.Equal(JobStatus.Running, renewedJob.Status);
        Assert.Equal("worker-1", renewedJob.ClaimedBy);
        Assert.Equal(claimedAt, renewedJob.ClaimedAt);
        Assert.Equal(renewedLeaseExpiresAt, renewedJob.LeaseExpiresAt);
        Assert.Equal(job.CurrentStateChangeId, renewedJob.CurrentStateChangeId);
        Assert.Equal(job.History.Count, renewedJob.History.Count);
    }

    [Fact]
    public void AcknowledgeSetsActorAndTimestampWithoutChangingHistory()
    {
        var enqueuedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);
        var acknowledgedAt = enqueuedAt.AddMinutes(1);
        var job = JobRecord.Enqueue(
            Guid.NewGuid(),
            TenantId,
            ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            enqueuedAt);

        var acknowledgedJob = job.Acknowledge("manager-alpha", acknowledgedAt);

        Assert.Equal("manager-alpha", acknowledgedJob.AcknowledgedBy);
        Assert.Equal(acknowledgedAt, acknowledgedJob.AcknowledgedAt);
        Assert.Equal(job.History.Count, acknowledgedJob.History.Count);
        Assert.Equal(job.CurrentStateChangeId, acknowledgedJob.CurrentStateChangeId);
    }

    [Fact]
    public void TransitionToFailedAppendsHistoryAndCapturesReason()
    {
        var enqueuedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);
        var runningAt = enqueuedAt.AddSeconds(5);
        var failedAt = runningAt.AddSeconds(2);
        var job = JobRecord.Enqueue(
            Guid.NewGuid(),
            TenantId,
            ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            enqueuedAt);

        var runningJob = job.TransitionTo(JobStatus.Running, runningAt);
        var failedJob = runningJob.TransitionToFailed("SMTP server unavailable.", failedAt);

        Assert.Equal(JobStatus.Failed, failedJob.Status);
        Assert.Null(failedJob.RunAt);
        Assert.Null(failedJob.ClaimedBy);
        Assert.Null(failedJob.ClaimedAt);
        Assert.Null(failedJob.LeaseExpiresAt);
        Assert.Equal(failedJob.History[^1].Id, failedJob.CurrentStateChangeId);
        Assert.Equal("SMTP server unavailable.", failedJob.FailureReason);
        Assert.Equal(1, failedJob.AttemptCount);
        Assert.Equal(runningAt, failedJob.StartedAt);
        Assert.Null(failedJob.CompletedAt);
        Assert.Equal(failedAt, failedJob.FailedAt);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Failed],
            failedJob.History.Select(change => change.Status));
        Assert.Equal(failedAt, failedJob.History[^1].ChangedAt);
        Assert.Equal("SMTP server unavailable.", failedJob.History[^1].Reason);

        var attempt = Assert.Single(failedJob.Attempts);
        Assert.Equal(1, attempt.Number);
        Assert.Equal(JobStatus.Failed, attempt.Status);
        Assert.Equal(runningAt, attempt.StartedAt);
        Assert.Null(attempt.CompletedAt);
        Assert.Equal(failedAt, attempt.FailedAt);
        Assert.Equal(TimeSpan.FromSeconds(2), attempt.Duration);
        Assert.Equal("SMTP server unavailable.", attempt.FailureReason);
    }

    [Fact]
    public void ScheduleRetryAppendsFailureAndScheduledHistory()
    {
        var enqueuedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);
        var failedAt = enqueuedAt.AddSeconds(2);
        var scheduledAt = failedAt.AddSeconds(10);
        var job = JobRecord.Enqueue(
            Guid.NewGuid(),
            TenantId,
            ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            enqueuedAt);
        var runningJob = job.TransitionTo(JobStatus.Running, enqueuedAt.AddSeconds(1));

        var retriedJob = runningJob.ScheduleRetry(
            "SMTP server unavailable.",
            failedAt,
            scheduledAt);

        Assert.Equal(JobStatus.Scheduled, retriedJob.Status);
        Assert.Equal(scheduledAt, retriedJob.RunAt);
        Assert.Null(retriedJob.ClaimedBy);
        Assert.Null(retriedJob.ClaimedAt);
        Assert.Null(retriedJob.LeaseExpiresAt);
        Assert.Equal(retriedJob.History[^1].Id, retriedJob.CurrentStateChangeId);
        Assert.Equal("SMTP server unavailable.", retriedJob.FailureReason);
        Assert.Equal(1, retriedJob.AttemptCount);
        Assert.Equal(failedAt, retriedJob.FailedAt);
        Assert.Equal(scheduledAt, retriedJob.ScheduledAt);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Failed, JobStatus.Scheduled],
            retriedJob.History.Select(change => change.Status));
        Assert.Equal("SMTP server unavailable.", retriedJob.History[^2].Reason);
        Assert.Equal("Retry scheduled.", retriedJob.History[^1].Reason);
        Assert.Equal(scheduledAt, retriedJob.History[^1].ScheduledAt);

        var attempt = Assert.Single(retriedJob.Attempts);
        Assert.Equal(1, attempt.Number);
        Assert.Equal(JobStatus.Failed, attempt.Status);
        Assert.Equal("SMTP server unavailable.", attempt.FailureReason);
    }

    [Fact]
    public void AttemptsProjectMultipleRunsFromStateHistory()
    {
        var enqueuedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);
        var firstStartedAt = enqueuedAt.AddSeconds(1);
        var firstFailedAt = enqueuedAt.AddSeconds(3);
        var retryScheduledAt = enqueuedAt.AddSeconds(10);
        var secondStartedAt = enqueuedAt.AddSeconds(11);
        var completedAt = enqueuedAt.AddSeconds(14);
        var job = JobRecord.Enqueue(
            Guid.NewGuid(),
            TenantId,
            ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            enqueuedAt);

        var completedJob = job
            .TransitionTo(JobStatus.Running, firstStartedAt)
            .ScheduleRetry("SMTP server unavailable.", firstFailedAt, retryScheduledAt)
            .TransitionTo(JobStatus.Queued, retryScheduledAt)
            .TransitionTo(JobStatus.Running, secondStartedAt)
            .TransitionTo(JobStatus.Completed, completedAt);

        Assert.Equal(2, completedJob.AttemptCount);
        Assert.Collection(
            completedJob.Attempts,
            firstAttempt =>
            {
                Assert.Equal(1, firstAttempt.Number);
                Assert.Equal(JobStatus.Failed, firstAttempt.Status);
                Assert.Equal(firstStartedAt, firstAttempt.StartedAt);
                Assert.Equal(firstFailedAt, firstAttempt.FailedAt);
                Assert.Equal(TimeSpan.FromSeconds(2), firstAttempt.Duration);
                Assert.Equal("SMTP server unavailable.", firstAttempt.FailureReason);
            },
            secondAttempt =>
            {
                Assert.Equal(2, secondAttempt.Number);
                Assert.Equal(JobStatus.Completed, secondAttempt.Status);
                Assert.Equal(secondStartedAt, secondAttempt.StartedAt);
                Assert.Equal(completedAt, secondAttempt.CompletedAt);
                Assert.Equal(TimeSpan.FromSeconds(3), secondAttempt.Duration);
                Assert.Null(secondAttempt.FailureReason);
            });
    }

    [Fact]
    public void EnqueueRejectsMaxAttemptsLessThanOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobRecord.Enqueue(
            Guid.NewGuid(),
            TenantId,
            ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 0,
            new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void ScheduleRejectsMaxAttemptsLessThanOne()
    {
        var now = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(() => JobRecord.Schedule(
            Guid.NewGuid(),
            TenantId,
            ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 0,
            now.AddSeconds(30),
            now));
    }

    [Fact]
    public void EnqueueRejectsBlankTenantId()
    {
        Assert.Throws<ArgumentException>(() => JobRecord.Enqueue(
            Guid.NewGuid(),
            "",
            ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void ScheduleRejectsBlankCreatedByActorId()
    {
        var now = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentException>(() => JobRecord.Schedule(
            Guid.NewGuid(),
            TenantId,
            "",
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            now.AddSeconds(30),
            now));
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse("""{"userId":"user_123"}""");
        return document.RootElement.Clone();
    }
}
