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
        store.Add(queuedJob);
        store.Add(completedJob);
        store.Add(failedJob);
        store.TryClaimNextQueuedJob();
        store.MarkCompleted(completedJob.Id);
        store.TryClaimNextQueuedJob();
        store.MarkFailed(failedJob.Id, "SMTP server unavailable.");
        var model = new IndexModel(store);

        model.OnGet();

        Assert.Equal(3, model.Jobs.Count);
        Assert.Equal(1, model.QueuedCount);
        Assert.Equal(0, model.RunningCount);
        Assert.Equal(1, model.CompletedCount);
        Assert.Equal(1, model.FailedCount);

        var failedSummary = model.Jobs.Single(job => job.Id == failedJob.Id);
        Assert.Equal(JobStatus.Failed, failedSummary.Status);
        Assert.Equal("SMTP server unavailable.", failedSummary.FailureReason);
        Assert.Equal(failedJob.EnqueuedAt, failedSummary.EnqueuedAt);
        Assert.NotNull(failedSummary.StartedAt);
        Assert.Null(failedSummary.CompletedAt);
        Assert.NotNull(failedSummary.FailedAt);
        Assert.Equal(1, failedSummary.AttemptCount);
        Assert.Equal(3, failedSummary.MaxAttempts);
        Assert.True(failedSummary.RetryAvailable);
        Assert.Equal($"/api/jobs/{failedJob.Id}", failedSummary.StatusUrl);

        var queuedSummary = model.Jobs.Single(job => job.Id == queuedJob.Id);
        Assert.Null(queuedSummary.StartedAt);
        Assert.Null(queuedSummary.CompletedAt);
        Assert.Null(queuedSummary.FailedAt);
        Assert.Equal(0, queuedSummary.AttemptCount);
        Assert.Equal(3, queuedSummary.MaxAttempts);
        Assert.False(queuedSummary.RetryAvailable);

        var completedSummary = model.Jobs.Single(job => job.Id == completedJob.Id);
        Assert.Equal(JobStatus.Completed, completedSummary.Status);
        Assert.NotNull(completedSummary.StartedAt);
        Assert.NotNull(completedSummary.CompletedAt);
        Assert.Null(completedSummary.FailedAt);
        Assert.Equal(1, completedSummary.AttemptCount);
        Assert.Equal(3, completedSummary.MaxAttempts);
        Assert.False(completedSummary.RetryAvailable);
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

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse("""{"userId":"user_123"}""");
        return document.RootElement.Clone();
    }
}
