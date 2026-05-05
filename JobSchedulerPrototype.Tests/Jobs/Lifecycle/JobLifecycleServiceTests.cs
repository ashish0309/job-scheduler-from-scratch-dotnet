using System.Text.Json;
using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class JobLifecycleServiceTests
{
    [Fact]
    public void EnqueueCreatesQueuedJobForValidImmediateRequest()
    {
        var store = new InMemoryJobStore();
        var lifecycle = CreateLifecycle(store);

        var result = lifecycle.Enqueue(
            "send-welcome-email",
            Payload(),
            delaySeconds: null);

        Assert.True(result.Accepted);
        Assert.NotNull(result.Job);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(JobStatus.Queued, result.Job.Status);
        Assert.Equal(3, result.Job.MaxAttempts);
        Assert.Equal(result.Job, store.Get(result.Job.Id));
    }

    [Fact]
    public void EnqueueCreatesScheduledJobForValidDelayedRequest()
    {
        var store = new InMemoryJobStore();
        var lifecycle = CreateLifecycle(store);

        var result = lifecycle.Enqueue(
            "send-welcome-email",
            Payload(),
            delaySeconds: 30);

        Assert.True(result.Accepted);
        Assert.NotNull(result.Job);
        Assert.Equal(JobStatus.Scheduled, result.Job.Status);
        Assert.NotNull(result.Job.ScheduledAt);
        Assert.Equal(result.Job, store.Get(result.Job.Id));
    }

    [Theory]
    [InlineData("", """{"userId":"user_123","email":"person@example.com"}""", null, "Job type is required.")]
    [InlineData("not-a-real-job", """{}""", null, "Unsupported job type.")]
    [InlineData("send-welcome-email", """{"email":"person@example.com"}""", null, "User ID is required.")]
    [InlineData("send-welcome-email", """{"userId":"user_123","email":"person@example.com"}""", -1, "Delay seconds cannot be negative.")]
    [InlineData("send-welcome-email", """{"userId":"user_123","email":"person@example.com"}""", 3601, "Delay seconds exceeds the maximum allowed for this job type.")]
    public void EnqueueRejectsInvalidRequests(
        string type,
        string payloadJson,
        int? delaySeconds,
        string expectedErrorMessage)
    {
        var store = new InMemoryJobStore();
        var lifecycle = CreateLifecycle(store);

        var result = lifecycle.Enqueue(
            type,
            Payload(payloadJson),
            delaySeconds);

        Assert.False(result.Accepted);
        Assert.Null(result.Job);
        Assert.Equal(expectedErrorMessage, result.ErrorMessage);
        Assert.Empty(store.List());
    }

    [Fact]
    public void ClaimNextDueJobClaimsRunnableJob()
    {
        var store = new InMemoryJobStore();
        var lifecycle = CreateLifecycle(store);
        var enqueued = lifecycle.Enqueue(
            "send-welcome-email",
            Payload(),
            delaySeconds: null);
        var claimedAt = DateTimeOffset.UtcNow.AddSeconds(1);
        var leaseExpiresAt = claimedAt.AddMinutes(1);

        var claimedJob = lifecycle.ClaimNextDueJob(claimedAt, "worker-1", leaseExpiresAt);

        Assert.NotNull(enqueued.Job);
        Assert.NotNull(claimedJob);
        Assert.Equal(enqueued.Job.Id, claimedJob.Id);
        Assert.Equal(JobStatus.Running, claimedJob.Status);
        Assert.Equal("worker-1", claimedJob.ClaimedBy);
        Assert.Equal(claimedAt, claimedJob.ClaimedAt);
        Assert.Equal(leaseExpiresAt, claimedJob.LeaseExpiresAt);
    }

    [Fact]
    public void RenewLeaseExtendsClaimedJobLease()
    {
        var store = new InMemoryJobStore();
        var lifecycle = CreateLifecycle(store);
        var job = RunningJob(store);
        var renewedAt = new DateTimeOffset(2026, 5, 4, 10, 1, 30, TimeSpan.Zero);
        var renewedLeaseExpiresAt = renewedAt.AddMinutes(1);

        var renewed = lifecycle.RenewLease(job, renewedAt, renewedLeaseExpiresAt);

        Assert.True(renewed);
        Assert.Equal(renewedLeaseExpiresAt, store.Get(job.Id)?.LeaseExpiresAt);
    }

    [Fact]
    public void CompleteExecutionMarksJobCompletedWhenExecutionSucceeds()
    {
        var store = new InMemoryJobStore();
        var lifecycle = CreateLifecycle(store);
        var job = RunningJob(store);

        var completion = lifecycle.CompleteExecution(job, JobExecutionResult.Success());

        Assert.Equal(JobExecutionCompletionStatus.Completed, completion.Status);
        Assert.Equal(JobStatus.Completed, store.Get(job.Id)?.Status);
        Assert.Null(completion.FailureReason);
        Assert.Null(completion.RetryScheduledAt);
    }

    [Fact]
    public void CompleteExecutionSchedulesRetryWhenExecutionFailsAndAttemptsRemain()
    {
        var store = new InMemoryJobStore();
        var lifecycle = CreateLifecycle(store);
        var job = RunningJob(store);

        var completion = lifecycle.CompleteExecution(
            job,
            JobExecutionResult.Failure("SMTP server unavailable."));

        var retriedJob = store.Get(job.Id);
        Assert.Equal(JobExecutionCompletionStatus.RetryScheduled, completion.Status);
        Assert.Equal("SMTP server unavailable.", completion.FailureReason);
        Assert.NotNull(completion.RetryScheduledAt);
        Assert.NotNull(retriedJob);
        Assert.Equal(JobStatus.Scheduled, retriedJob.Status);
        Assert.Equal("SMTP server unavailable.", retriedJob.FailureReason);
    }

    [Fact]
    public void CompleteExecutionMarksJobFailedWhenExecutionFailsAndAttemptsAreExhausted()
    {
        var store = new InMemoryJobStore();
        var lifecycle = CreateLifecycle(store);
        var job = RunningJob(store, maxAttempts: 1);

        var completion = lifecycle.CompleteExecution(
            job,
            JobExecutionResult.Failure("SMTP server unavailable."));

        Assert.Equal(JobExecutionCompletionStatus.Failed, completion.Status);
        Assert.Equal("SMTP server unavailable.", completion.FailureReason);
        Assert.Null(completion.RetryScheduledAt);
        Assert.Equal(JobStatus.Failed, store.Get(job.Id)?.Status);
    }

    [Fact]
    public void CompleteExecutionReportsLeaseLostWhenClaimTokenIsStale()
    {
        var store = new InMemoryJobStore();
        var lifecycle = CreateLifecycle(store);
        var job = RunningJob(store);
        Assert.NotNull(job.LeaseExpiresAt);
        var reclaimedJob = store.TryClaimNextDueJob(
            job.LeaseExpiresAt.Value,
            "worker-2",
            job.LeaseExpiresAt.Value.AddMinutes(1));
        Assert.NotNull(reclaimedJob);

        var completion = lifecycle.CompleteExecution(job, JobExecutionResult.Success());

        Assert.Equal(JobExecutionCompletionStatus.LeaseLost, completion.Status);
        var persistedJob = store.Get(job.Id);
        Assert.NotNull(persistedJob);
        Assert.Equal(JobStatus.Running, persistedJob.Status);
        Assert.Equal("worker-2", persistedJob.ClaimedBy);
        Assert.Equal(reclaimedJob.CurrentStateChangeId, persistedJob.CurrentStateChangeId);
    }

    private static JobRecord RunningJob(InMemoryJobStore store, int maxAttempts = 3)
    {
        var job = JobRecord.Enqueue(
            Guid.NewGuid(),
            "send-welcome-email",
            Payload(),
            maxAttempts,
            new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));
        store.Add(job);
        return store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 1, 0, TimeSpan.Zero), "worker-1", (new DateTimeOffset(2026, 5, 4, 10, 1, 0, TimeSpan.Zero)).AddMinutes(1))!;
    }

    private static JobLifecycleService CreateLifecycle(IJobStore store)
    {
        return new JobLifecycleService(
            store,
            new JobDefinitionRegistry([new SendWelcomeEmailJobDefinition()]));
    }

    private static JsonElement Payload(
        string json = """{"userId":"user_123","email":"person@example.com"}""")
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
