using System.Text.Json;
using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class WorkerLifecycleActionsTests
{
    [Fact]
    public async Task ClaimNextDueJobActionClaimsJobForAuthorizedActor()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        store.Add(job);

        var action = new ClaimNextDueJobAction(
            LifecycleService(store),
            new TestJobActorProvider(),
            new JobAuthorizationRuleEvaluator());

        var now = new DateTimeOffset(2026, 5, 7, 20, 0, 0, TimeSpan.Zero);
        var result = await action.ExecuteAsync(new ClaimNextDueJobActionRequest(
            now,
            "worker-1",
            now.AddMinutes(1)));

        Assert.True(result.IsAuthorized);
        Assert.NotNull(result.Job);
        Assert.Equal(JobStatus.Running, result.Job.Status);
    }

    [Fact]
    public async Task ClaimNextDueJobActionRejectsWhenActorLacksExecutePermission()
    {
        var store = new InMemoryJobStore();
        store.Add(CreateJob());

        var action = new ClaimNextDueJobAction(
            LifecycleService(store),
            new TestJobActorProvider(new JobActor(
                TestJobActorProvider.ActorId,
                TestJobActorProvider.TenantId,
                [JobPermissions.EmailRead])),
            new JobAuthorizationRuleEvaluator());

        var now = new DateTimeOffset(2026, 5, 7, 20, 0, 0, TimeSpan.Zero);
        var result = await action.ExecuteAsync(new ClaimNextDueJobActionRequest(
            now,
            "worker-1",
            now.AddMinutes(1)));

        Assert.False(result.IsAuthorized);
        Assert.Equal("Actor is not authorized to claim jobs.", result.ErrorMessage);
        Assert.Null(result.Job);
    }

    [Fact]
    public async Task RenewJobLeaseActionRenewsLeaseWhenActorIsAuthorized()
    {
        var store = new InMemoryJobStore();
        var job = CreateClaimedJob(store);

        var action = new RenewJobLeaseAction(
            LifecycleService(store),
            new TestJobActorProvider(),
            new JobAuthorizationRuleEvaluator());

        var renewedAt = new DateTimeOffset(2026, 5, 7, 20, 2, 0, TimeSpan.Zero);
        var result = await action.ExecuteAsync(new RenewJobLeaseActionRequest(
            job,
            renewedAt,
            renewedAt.AddMinutes(1)));

        Assert.True(result.IsAuthorized);
        Assert.True(result.Renewed);
    }

    [Fact]
    public async Task CompleteJobExecutionActionCompletesWhenExecutionSucceeds()
    {
        var store = new InMemoryJobStore();
        var job = CreateClaimedJob(store);

        var action = new CompleteJobExecutionAction(
            LifecycleService(store),
            new TestJobActorProvider(),
            new JobAuthorizationRuleEvaluator());

        var result = await action.ExecuteAsync(new CompleteJobExecutionActionRequest(
            job,
            JobExecutionResult.Success()));

        Assert.True(result.IsAuthorized);
        Assert.NotNull(result.Completion);
        Assert.Equal(JobExecutionCompletionStatus.Completed, result.Completion.Status);
        Assert.Equal(JobStatus.Completed, store.Get(job.Id)?.Status);
    }

    private static IJobLifecycleService LifecycleService(IJobStore store)
    {
        return new JobLifecycleService(
            store,
            new JobDefinitionRegistry([new SendWelcomeEmailJobDefinition()]),
            new TestJobActorProvider());
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
            new DateTimeOffset(2026, 5, 7, 19, 0, 0, TimeSpan.Zero));
    }

    private static JobRecord CreateClaimedJob(IJobStore store)
    {
        var job = CreateJob();
        store.Add(job);

        var claimedAt = new DateTimeOffset(2026, 5, 7, 20, 1, 0, TimeSpan.Zero);
        return store.TryClaimNextDueJob(
                claimedAt,
                "worker-1",
                claimedAt.AddMinutes(1))
            ?? throw new InvalidOperationException("Expected job to be claimed.");
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse("""{"userId":"user_123","email":"person@example.com"}""");
        return document.RootElement.Clone();
    }
}
