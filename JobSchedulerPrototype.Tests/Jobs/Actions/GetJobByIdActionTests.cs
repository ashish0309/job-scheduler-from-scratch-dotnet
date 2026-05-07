using System.Text.Json;
using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class GetJobByIdActionTests
{
    [Fact]
    public async Task ExecuteAsyncReturnsJobForAuthorizedActor()
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

        var action = new GetJobByIdAction(
            store,
            new TestJobActorProvider(),
            new JobAuthorizationRuleEvaluator());

        var result = await action.ExecuteAsync(new GetJobByIdActionRequest(job.Id));

        Assert.True(result.IsAuthorized);
        Assert.NotNull(result.Job);
        Assert.Equal(job.Id, result.Job.Id);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsWhenActorLacksReadPermission()
    {
        var store = new InMemoryJobStore();
        var actorProvider = new TestJobActorProvider(new JobActor(
            TestJobActorProvider.ActorId,
            TestJobActorProvider.TenantId,
            [JobPermissions.EmailEnqueue]));
        var action = new GetJobByIdAction(
            store,
            actorProvider,
            new JobAuthorizationRuleEvaluator());

        var result = await action.ExecuteAsync(
            new GetJobByIdActionRequest(Guid.NewGuid()));

        Assert.False(result.IsAuthorized);
        Assert.Equal("Actor is not authorized to read jobs.", result.ErrorMessage);
        Assert.Null(result.Job);
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse("""{"userId":"user_123","email":"person@example.com"}""");
        return document.RootElement.Clone();
    }
}
