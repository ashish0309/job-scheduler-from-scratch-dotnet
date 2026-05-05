using System.Text.Json;
using JobSchedulerPrototype.Jobs;
using JobSchedulerPrototype.Pages;

namespace JobSchedulerPrototype.Tests.Pages;

public sealed class IndexModelTests
{
    [Fact]
    public void OnGetLoadsJobsAndStatusCounts()
    {
        var store = new InMemoryJobStore();
        var completedJob = CreateJob(enqueuedAt: new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));
        var failedJob = CreateJob(enqueuedAt: new DateTimeOffset(2026, 5, 4, 10, 1, 0, TimeSpan.Zero));
        var queuedJob = CreateJob(enqueuedAt: new DateTimeOffset(2026, 5, 4, 10, 2, 0, TimeSpan.Zero));
        var scheduledAt = new DateTimeOffset(2026, 5, 4, 10, 10, 0, TimeSpan.Zero);
        var scheduledJob = CreateScheduledJob(scheduledAt);
        store.Add(queuedJob);
        store.Add(completedJob);
        store.Add(failedJob);
        store.Add(scheduledJob);
        var completedRunningJob = store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero), "worker-1", (new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero)).AddMinutes(1));
        Assert.NotNull(completedRunningJob);
        store.MarkCompleted(completedJob.Id, completedRunningJob.CurrentStateChangeId);
        var failedRunningJob = store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero), "worker-1", (new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero)).AddMinutes(1));
        Assert.NotNull(failedRunningJob);
        store.MarkFailed(failedJob.Id, failedRunningJob.CurrentStateChangeId, "SMTP server unavailable.");
        var model = new IndexModel(store);

        model.OnGet();

        Assert.Equal(4, model.Jobs.Count);
        Assert.Equal(1, model.QueuedCount);
        Assert.Equal(0, model.RunningCount);
        Assert.Equal(1, model.CompletedCount);
        Assert.Equal(1, model.FailedCount);

        var failedSummary = model.Jobs.Single(job => job.Id == failedJob.Id);
        Assert.Equal(JobStatus.Failed, failedSummary.Status);
        Assert.Null(failedSummary.ClaimedBy);
        Assert.Null(failedSummary.ClaimedAt);
        Assert.Null(failedSummary.LeaseExpiresAt);
        Assert.Equal("SMTP server unavailable.", failedSummary.FailureReason);
        Assert.Equal(failedJob.EnqueuedAt, failedSummary.EnqueuedAt);
        Assert.NotNull(failedSummary.StartedAt);
        Assert.Null(failedSummary.CompletedAt);
        Assert.NotNull(failedSummary.FailedAt);
        Assert.Equal(1, failedSummary.AttemptCount);
        Assert.Equal(3, failedSummary.MaxAttempts);
        var failedAttempt = Assert.Single(failedSummary.Attempts);
        Assert.Equal(1, failedAttempt.Number);
        Assert.Equal(JobStatus.Failed, failedAttempt.Status);
        Assert.NotNull(failedAttempt.FailedAt);
        Assert.Equal("SMTP server unavailable.", failedAttempt.FailureReason);
        Assert.Equal(3, failedSummary.History.Count);
        Assert.Equal(failedSummary.CurrentStateChangeId, failedSummary.History[^1].Id);
        Assert.Equal(JobStatus.Failed, failedSummary.History[^1].Status);
        Assert.Equal("SMTP server unavailable.", failedSummary.History[^1].Reason);
        Assert.Equal($"/api/jobs/{failedJob.Id}", failedSummary.StatusUrl);

        var queuedSummary = model.Jobs.Single(job => job.Id == queuedJob.Id);
        Assert.Null(queuedSummary.ClaimedBy);
        Assert.Null(queuedSummary.ClaimedAt);
        Assert.Null(queuedSummary.LeaseExpiresAt);
        Assert.Null(queuedSummary.StartedAt);
        Assert.Null(queuedSummary.CompletedAt);
        Assert.Null(queuedSummary.FailedAt);
        Assert.Equal(0, queuedSummary.AttemptCount);
        Assert.Equal(3, queuedSummary.MaxAttempts);
        Assert.Empty(queuedSummary.Attempts);
        var queuedStateChange = Assert.Single(queuedSummary.History);
        Assert.Equal(queuedSummary.CurrentStateChangeId, queuedStateChange.Id);
        Assert.Equal(JobStatus.Queued, queuedStateChange.Status);
        Assert.Equal("Job accepted.", queuedStateChange.Reason);

        var completedSummary = model.Jobs.Single(job => job.Id == completedJob.Id);
        Assert.Equal(JobStatus.Completed, completedSummary.Status);
        Assert.Null(completedSummary.ClaimedBy);
        Assert.Null(completedSummary.ClaimedAt);
        Assert.Null(completedSummary.LeaseExpiresAt);
        Assert.NotNull(completedSummary.StartedAt);
        Assert.NotNull(completedSummary.CompletedAt);
        Assert.Null(completedSummary.FailedAt);
        Assert.Equal(1, completedSummary.AttemptCount);
        Assert.Equal(3, completedSummary.MaxAttempts);
        var completedAttempt = Assert.Single(completedSummary.Attempts);
        Assert.Equal(1, completedAttempt.Number);
        Assert.Equal(JobStatus.Completed, completedAttempt.Status);
        Assert.NotNull(completedAttempt.CompletedAt);
        Assert.Null(completedAttempt.FailureReason);
        Assert.Equal(3, completedSummary.History.Count);
        Assert.Equal(completedSummary.CurrentStateChangeId, completedSummary.History[^1].Id);
        Assert.Equal(JobStatus.Completed, completedSummary.History[^1].Status);
        Assert.Equal("Job completed successfully.", completedSummary.History[^1].Reason);

        var scheduledSummary = model.Jobs.Single(job => job.Id == scheduledJob.Id);
        Assert.Equal(JobStatus.Scheduled, scheduledSummary.Status);
        Assert.Null(scheduledSummary.ClaimedBy);
        Assert.Null(scheduledSummary.ClaimedAt);
        Assert.Null(scheduledSummary.LeaseExpiresAt);
        Assert.Equal(scheduledAt, scheduledSummary.ScheduledAt);
        Assert.Null(scheduledSummary.StartedAt);
        Assert.Empty(scheduledSummary.Attempts);
        var scheduledStateChange = Assert.Single(scheduledSummary.History);
        Assert.Equal(scheduledSummary.CurrentStateChangeId, scheduledStateChange.Id);
        Assert.Equal(JobStatus.Scheduled, scheduledStateChange.Status);
        Assert.Equal(scheduledAt, scheduledStateChange.ScheduledAt);
        Assert.Equal("Job scheduled.", scheduledStateChange.Reason);
    }

    private static JobRecord CreateJob(DateTimeOffset enqueuedAt)
    {
        return JobRecord.Enqueue(
            Guid.NewGuid(),
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            enqueuedAt);
    }

    private static JobRecord CreateScheduledJob(DateTimeOffset scheduledAt)
    {
        return JobRecord.Schedule(
            Guid.NewGuid(),
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            scheduledAt,
            new DateTimeOffset(2026, 5, 4, 10, 3, 0, TimeSpan.Zero));
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse("""{"userId":"user_123"}""");
        return document.RootElement.Clone();
    }
}
