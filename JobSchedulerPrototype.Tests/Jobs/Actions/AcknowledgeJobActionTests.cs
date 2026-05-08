using System.Text.Json;
using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class AcknowledgeJobActionTests
{
    [Fact]
    public async Task ExecuteAsyncAcknowledgesJobForAuthorizedActor()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        store.Add(job);

        var action = new AcknowledgeJobAction(
            store,
            new TestJobActorProvider(new JobActor(
                "manager-alpha",
                TestJobActorProvider.TenantId,
                [JobPermissions.EmailRead, JobPermissions.EmailManage])),
            new JobAuthorizationRuleEvaluator(),
            ScopeProvider());

        var result = await action.ExecuteAsync(new AcknowledgeJobActionRequest(job.Id));

        Assert.True(result.IsAuthorized);
        Assert.True(result.Acknowledged);
        Assert.Null(result.ErrorMessage);

        var acknowledgedJob = store.Get(job.Id);
        Assert.NotNull(acknowledgedJob);
        Assert.Equal("manager-alpha", acknowledgedJob.AcknowledgedBy);
        Assert.NotNull(acknowledgedJob.AcknowledgedAt);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsOwnerWithoutManagePermission()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        store.Add(job);

        var action = new AcknowledgeJobAction(
            store,
            new TestJobActorProvider(new JobActor(
                TestJobActorProvider.ActorId,
                TestJobActorProvider.TenantId,
                [JobPermissions.EmailRead])),
            new JobAuthorizationRuleEvaluator(),
            ScopeProvider());

        var result = await action.ExecuteAsync(new AcknowledgeJobActionRequest(job.Id));

        Assert.False(result.IsAuthorized);
        Assert.False(result.Acknowledged);
        Assert.Equal("Actor is not authorized to acknowledge jobs.", result.ErrorMessage);

        var persistedJob = store.Get(job.Id);
        Assert.NotNull(persistedJob);
        Assert.Null(persistedJob.AcknowledgedBy);
        Assert.Null(persistedJob.AcknowledgedAt);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsWhenActorIsNotOwnerAndLacksManagePermission()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        store.Add(job);

        var action = new AcknowledgeJobAction(
            store,
            new TestJobActorProvider(new JobActor(
                "viewer-alpha",
                TestJobActorProvider.TenantId,
                [JobPermissions.EmailRead])),
            new JobAuthorizationRuleEvaluator(),
            ScopeProvider());

        var result = await action.ExecuteAsync(new AcknowledgeJobActionRequest(job.Id));

        Assert.False(result.IsAuthorized);
        Assert.False(result.Acknowledged);
        Assert.Equal("Actor is not authorized to acknowledge jobs.", result.ErrorMessage);

        var persistedJob = store.Get(job.Id);
        Assert.NotNull(persistedJob);
        Assert.Null(persistedJob.AcknowledgedBy);
        Assert.Null(persistedJob.AcknowledgedAt);
    }

    private static JobRecord CreateJob()
    {
        return JobRecord.Enqueue(
            Guid.NewGuid(),
            TestJobActorProvider.TenantId,
            TestJobActorProvider.ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            new DateTimeOffset(2026, 5, 8, 10, 0, 0, TimeSpan.Zero));
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse("""{"userId":"user_123","email":"person@example.com"}""");
        return document.RootElement.Clone();
    }

    private static IDataAccessScopeProvider ScopeProvider()
    {
        return new FixedDataAccessScopeProvider(DataAccessScope.Tenant(TestJobActorProvider.TenantId));
    }
}
