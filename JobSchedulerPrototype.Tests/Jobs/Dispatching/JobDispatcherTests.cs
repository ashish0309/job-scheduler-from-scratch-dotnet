using System.Text.Json;
using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class JobDispatcherTests
{
    [Fact]
    public async Task ExecuteAsyncDispatchesJobToRegisteredHandler()
    {
        var handler = new CapturingJobHandler(
            JobTypes.SendWelcomeEmail,
            JobExecutionResult.Success());
        var dispatcher = new JobDispatcher(new JobHandlerRegistry([handler]));
        var job = CreateJob(JobTypes.SendWelcomeEmail);

        var result = await dispatcher.ExecuteAsync(job, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(job.Id, handler.Job?.Id);
    }

    [Fact]
    public async Task ExecuteAsyncFailsWhenJobTypeHasNoHandler()
    {
        var dispatcher = new JobDispatcher(new JobHandlerRegistry([]));
        var job = CreateJob("unsupported-job");

        var result = await dispatcher.ExecuteAsync(job, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("No executor is registered for this job type.", result.FailureReason);
    }

    private static JobRecord CreateJob(string type)
    {
        using var document = JsonDocument.Parse("""{"userId":"user_123","email":"person@example.com"}""");

        return JobRecord.Enqueue(
            Guid.NewGuid(),
            type,
            document.RootElement.Clone(),
            new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));
    }

    private sealed class CapturingJobHandler : IJobHandler
    {
        private readonly JobExecutionResult _result;

        public CapturingJobHandler(string type, JobExecutionResult result)
        {
            Type = type;
            _result = result;
        }

        public string Type { get; }

        public JobRecord? Job { get; private set; }

        public Task<JobExecutionResult> ExecuteAsync(JobRecord job, CancellationToken cancellationToken)
        {
            Job = job;
            return Task.FromResult(_result);
        }
    }
}
