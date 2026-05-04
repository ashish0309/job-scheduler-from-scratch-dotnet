using System.Text.Json;
using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class SendWelcomeEmailJobHandlerTests
{
    [Fact]
    public async Task ExecuteAsyncSucceedsWhenPayloadDoesNotRequestFailure()
    {
        var handler = new SendWelcomeEmailJobHandler();
        var job = CreateJob("""{"userId":"user_123","email":"person@example.com"}""");

        var result = await handler.ExecuteAsync(job, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async Task ExecuteAsyncFailsWhenPayloadRequestsFailure()
    {
        var handler = new SendWelcomeEmailJobHandler();
        var job = CreateJob("""{"userId":"user_123","email":"person@example.com","shouldFail":true}""");

        var result = await handler.ExecuteAsync(job, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Simulated welcome email failure.", result.FailureReason);
    }

    [Fact]
    public async Task ExecuteAsyncFailsWhenStoredPayloadIsInvalid()
    {
        var handler = new SendWelcomeEmailJobHandler();
        var job = CreateJob("""{"userId":true}""");

        var result = await handler.ExecuteAsync(job, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Stored job payload is invalid.", result.FailureReason);
    }

    private static JobRecord CreateJob(string payload)
    {
        using var document = JsonDocument.Parse(payload);

        return JobRecord.Enqueue(
            Guid.NewGuid(),
            JobTypes.SendWelcomeEmail,
            document.RootElement.Clone(),
            new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));
    }
}
