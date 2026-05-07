using System.Text.Json;
using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class EnqueueJobActionTests
{
    [Fact]
    public async Task ExecuteAsyncAcceptsValidRequestForAuthorizedActor()
    {
        var store = new InMemoryJobStore();
        var definitions = new JobDefinitionRegistry([new SendWelcomeEmailJobDefinition()]);
        var action = new EnqueueJobAction(
            store,
            definitions,
            new TestJobActorProvider(),
            new JobAuthorizationRuleEvaluator());

        var result = await action.ExecuteAsync(
            "send-welcome-email",
            Payload(),
            delaySeconds: null);

        Assert.True(result.Accepted);
        Assert.NotNull(result.Job);
        Assert.Equal(JobStatus.Queued, result.Job.Status);
        Assert.Equal(TestJobActorProvider.ActorId, result.Job.CreatedByActorId);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsWhenActorLacksPermission()
    {
        var store = new InMemoryJobStore();
        var definitions = new JobDefinitionRegistry([new SendWelcomeEmailJobDefinition()]);
        var action = new EnqueueJobAction(
            store,
            definitions,
            new TestJobActorProvider(new JobActor(
                TestJobActorProvider.ActorId,
                TestJobActorProvider.TenantId,
                [JobPermissions.EmailRead])),
            new JobAuthorizationRuleEvaluator());

        var result = await action.ExecuteAsync(
            "send-welcome-email",
            Payload(),
            delaySeconds: null);

        Assert.False(result.Accepted);
        Assert.Equal("Actor is not authorized to enqueue jobs.", result.ErrorMessage);
        Assert.Empty(store.List());
    }

    [Fact]
    public async Task ExecuteAsyncRejectsInvalidRequestAfterAuthorization()
    {
        var store = new InMemoryJobStore();
        var definitions = new JobDefinitionRegistry([new SendWelcomeEmailJobDefinition()]);
        var action = new EnqueueJobAction(
            store,
            definitions,
            new TestJobActorProvider(),
            new JobAuthorizationRuleEvaluator());

        var result = await action.ExecuteAsync(
            "not-a-real-job",
            Payload(),
            delaySeconds: null);

        Assert.False(result.Accepted);
        Assert.Equal("Unsupported job type.", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsyncThrowsWhenActionDefinesNoAuthorizationRules()
    {
        var action = new EmptyRuleAction(
            new TestJobActorProvider(),
            new JobAuthorizationRuleEvaluator());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => action.ExecuteAsync(new EmptyRequest("request")));

        Assert.Contains("must define at least one authorization rule", exception.Message);
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse("""{"userId":"user_123","email":"person@example.com"}""");
        return document.RootElement.Clone();
    }

    private sealed record EmptyRequest(string Value) : IJobActionRequest<string>;

    private sealed class EmptyRuleAction : JobAuthorizedAction<EmptyRequest, string>
    {
        public EmptyRuleAction(
            IJobActorProvider actorProvider,
            IJobAuthorizationRuleEvaluator ruleEvaluator)
            : base(actorProvider, ruleEvaluator)
        {
        }

        protected override IReadOnlyList<IJobAuthorizationRule> BuildAuthorizationRules(EmptyRequest request)
        {
            return [];
        }

        protected override string OnAuthorizationDenied(JobAuthorizationResult result)
        {
            return "denied";
        }

        protected override Task<string> ExecuteAuthorizedAsync(
            EmptyRequest request,
            JobActor actor,
            CancellationToken cancellationToken)
        {
            return Task.FromResult("ok");
        }
    }
}
