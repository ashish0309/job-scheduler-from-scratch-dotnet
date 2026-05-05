using System.Text.Json;
using JobSchedulerPrototype.Jobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class QueuedJobWorkerTests
{
    [Fact]
    public async Task ProcessNextJobAsyncCompletesJobWhenExecutionSucceeds()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        store.Add(job);
        var worker = new QueuedJobWorker(
            LifecycleService(store),
            new StubJobDispatcher(JobExecutionResult.Success()),
            NullLogger<QueuedJobWorker>.Instance,
            pollInterval: TimeSpan.Zero,
            simulatedWorkDuration: TimeSpan.Zero);

        var processedJob = await worker.ProcessNextJobAsync(CancellationToken.None);

        Assert.True(processedJob);
        var completedJob = store.Get(job.Id);
        Assert.NotNull(completedJob);
        Assert.Equal(JobStatus.Completed, completedJob.Status);
        Assert.NotNull(completedJob.StartedAt);
        Assert.NotNull(completedJob.CompletedAt);
        Assert.Null(completedJob.FailedAt);
    }

    [Fact]
    public async Task ProcessNextJobAsyncSchedulesRetryWhenExecutionFailsAndAttemptsRemain()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        store.Add(job);
        var worker = new QueuedJobWorker(
            LifecycleService(store),
            new StubJobDispatcher(JobExecutionResult.Failure("Simulated welcome email failure.")),
            NullLogger<QueuedJobWorker>.Instance,
            pollInterval: TimeSpan.Zero,
            simulatedWorkDuration: TimeSpan.Zero);

        var processedJob = await worker.ProcessNextJobAsync(CancellationToken.None);

        Assert.True(processedJob);
        var retriedJob = store.Get(job.Id);
        Assert.NotNull(retriedJob);
        Assert.Equal(JobStatus.Scheduled, retriedJob.Status);
        Assert.Equal("Simulated welcome email failure.", retriedJob.FailureReason);
        Assert.NotNull(retriedJob.StartedAt);
        Assert.Null(retriedJob.CompletedAt);
        Assert.NotNull(retriedJob.FailedAt);
        Assert.NotNull(retriedJob.ScheduledAt);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Failed, JobStatus.Scheduled],
            retriedJob.History.Select(change => change.Status));
        Assert.Equal("Retry scheduled.", retriedJob.History[^1].Reason);
    }

    [Fact]
    public async Task ProcessNextJobAsyncFailsJobWhenExecutionFailsAndAttemptsAreExhausted()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob(maxAttempts: 1);
        store.Add(job);
        var worker = new QueuedJobWorker(
            LifecycleService(store),
            new StubJobDispatcher(JobExecutionResult.Failure("Simulated welcome email failure.")),
            NullLogger<QueuedJobWorker>.Instance,
            pollInterval: TimeSpan.Zero,
            simulatedWorkDuration: TimeSpan.Zero);

        var processedJob = await worker.ProcessNextJobAsync(CancellationToken.None);

        Assert.True(processedJob);
        var failedJob = store.Get(job.Id);
        Assert.NotNull(failedJob);
        Assert.Equal(JobStatus.Failed, failedJob.Status);
        Assert.Equal("Simulated welcome email failure.", failedJob.FailureReason);
        Assert.NotNull(failedJob.StartedAt);
        Assert.Null(failedJob.CompletedAt);
        Assert.NotNull(failedJob.FailedAt);
    }

    [Fact]
    public async Task ProcessNextJobAsyncReturnsFalseWhenNoQueuedJobExists()
    {
        var store = new InMemoryJobStore();
        var worker = new QueuedJobWorker(
            LifecycleService(store),
            new StubJobDispatcher(JobExecutionResult.Success()),
            NullLogger<QueuedJobWorker>.Instance,
            pollInterval: TimeSpan.Zero,
            simulatedWorkDuration: TimeSpan.Zero);

        var processedJob = await worker.ProcessNextJobAsync(CancellationToken.None);

        Assert.False(processedJob);
    }

    private static JobRecord CreateJob(int maxAttempts = 3)
    {
        return JobRecord.Enqueue(
            Guid.NewGuid(),
            "send-welcome-email",
            Payload(),
            maxAttempts,
            new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));
    }

    private static IJobDefinitionRegistry DefinitionRegistry()
    {
        return new JobDefinitionRegistry([new SendWelcomeEmailJobDefinition()]);
    }

    private static IJobLifecycleService LifecycleService(IJobStore store)
    {
        return new JobLifecycleService(store, DefinitionRegistry());
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse("""{"userId":"user_123","email":"person@example.com"}""");
        return document.RootElement.Clone();
    }

    private sealed class StubJobDispatcher : IJobDispatcher
    {
        private readonly JobExecutionResult _result;

        public StubJobDispatcher(JobExecutionResult result)
        {
            _result = result;
        }

        public Task<JobExecutionResult> ExecuteAsync(JobRecord job, CancellationToken cancellationToken)
        {
            return Task.FromResult(_result);
        }
    }
}
