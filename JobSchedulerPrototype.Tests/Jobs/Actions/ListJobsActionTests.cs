using System.Text.Json;
using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class ListJobsActionTests
{
    [Fact]
    public async Task ExecuteAsyncReturnsJobsForAuthorizedActor()
    {
        var store = new InMemoryJobStore();
        var now = new DateTimeOffset(2026, 5, 7, 20, 0, 0, TimeSpan.Zero);
        var job = JobRecord.Enqueue(
            Guid.NewGuid(),
            TestJobActorProvider.TenantId,
            TestJobActorProvider.ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            now);
        store.Add(job);

        var action = new ListJobsAction(
            store,
            new TestJobActorProvider(),
            new JobAuthorizationRuleEvaluator());

        var result = await action.ExecuteAsync(new ListJobsActionRequest());

        Assert.True(result.IsAuthorized);
        var listedJob = Assert.Single(result.Jobs);
        Assert.Equal(job.Id, listedJob.Id);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsWhenActorLacksReadPermission()
    {
        var store = new InMemoryJobStore();
        var actorProvider = new TestJobActorProvider(new JobActor(
            TestJobActorProvider.ActorId,
            TestJobActorProvider.TenantId,
            [JobPermissions.EmailEnqueue]));
        var action = new ListJobsAction(
            store,
            actorProvider,
            new JobAuthorizationRuleEvaluator());

        var result = await action.ExecuteAsync(new ListJobsActionRequest());

        Assert.False(result.IsAuthorized);
        Assert.Equal("Actor is not authorized to read jobs.", result.ErrorMessage);
        Assert.Empty(result.Jobs);
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse("""{"userId":"user_123","email":"person@example.com"}""");
        return document.RootElement.Clone();
    }
}
