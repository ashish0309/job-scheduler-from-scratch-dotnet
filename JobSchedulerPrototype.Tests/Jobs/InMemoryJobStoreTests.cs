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
    public void TryClaimNextQueuedJobClaimsOldestQueuedJob()
    {
        var store = new InMemoryJobStore();
        var earlierJob = CreateJob(enqueuedAt: new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));
        var laterJob = CreateJob(enqueuedAt: new DateTimeOffset(2026, 5, 4, 10, 1, 0, TimeSpan.Zero));
        store.Add(laterJob);
        store.Add(earlierJob);

        var claimedJob = store.TryClaimNextQueuedJob();

        Assert.NotNull(claimedJob);
        Assert.Equal(earlierJob.Id, claimedJob.Id);
        Assert.Equal(JobStatus.Running, claimedJob.Status);
        Assert.Equal([JobStatus.Queued, JobStatus.Running], claimedJob.History.Select(change => change.Status));
        Assert.Equal(JobStatus.Running, store.Get(earlierJob.Id)?.Status);
        Assert.Equal(JobStatus.Queued, store.Get(laterJob.Id)?.Status);
    }

    [Fact]
    public void TryClaimNextQueuedJobDoesNotClaimRunningJobAgain()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        store.Add(job);

        var firstClaim = store.TryClaimNextQueuedJob();
        var secondClaim = store.TryClaimNextQueuedJob();

        Assert.NotNull(firstClaim);
        Assert.Null(secondClaim);
    }

    [Fact]
    public void MarkCompletedOnlyCompletesRunningJobs()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        store.Add(job);

        var queuedCompletion = store.MarkCompleted(job.Id);
        var runningJob = store.TryClaimNextQueuedJob();
        var runningCompletion = store.MarkCompleted(job.Id);
        var completedJob = store.Get(job.Id);

        Assert.False(queuedCompletion);
        Assert.NotNull(runningJob);
        Assert.True(runningCompletion);
        Assert.NotNull(completedJob);
        Assert.Equal(JobStatus.Completed, completedJob.Status);
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

    private static JobRecord CreateJob(DateTimeOffset? enqueuedAt = null)
    {
        return JobRecord.Enqueue(
            Guid.NewGuid(),
            "send-welcome-email",
            Payload(),
            enqueuedAt ?? new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse("""{"userId":"user_123"}""");
        return document.RootElement.Clone();
    }
}
