using System.Text.Json;
using JobSchedulerPrototype.Jobs;

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
            store,
            new StubJobDispatcher(JobExecutionResult.Success()),
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
    public async Task ProcessNextJobAsyncFailsJobWhenExecutionFails()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        store.Add(job);
        var worker = new QueuedJobWorker(
            store,
            new StubJobDispatcher(JobExecutionResult.Failure("Simulated welcome email failure.")),
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
            store,
            new StubJobDispatcher(JobExecutionResult.Success()),
            pollInterval: TimeSpan.Zero,
            simulatedWorkDuration: TimeSpan.Zero);

        var processedJob = await worker.ProcessNextJobAsync(CancellationToken.None);

        Assert.False(processedJob);
    }

    private static JobRecord CreateJob()
    {
        return JobRecord.Enqueue(
            Guid.NewGuid(),
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));
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
