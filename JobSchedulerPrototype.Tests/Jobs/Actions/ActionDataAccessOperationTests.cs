using System.Text.Json;
using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class ActionDataAccessOperationTests
{
    [Fact]
    public async Task EnqueueActionExecutesUnderMutateOperationAndActorTenantScope()
    {
        var scopeProvider = ScopeProvider(JobPermissions.EmailEnqueue);
        var store = new ScopeCapturingJobStore(scopeProvider);
        var action = new EnqueueJobAction(
            store,
            new JobDefinitionRegistry([new SendWelcomeEmailJobDefinition()]),
            new TestJobActorProvider(),
            new JobAuthorizationRuleEvaluator(),
            scopeProvider);

        _ = await action.ExecuteAsync(
            "send-welcome-email",
            Payload(),
            delaySeconds: null);

        Assert.Equal(global::JobSchedulerPrototype.Jobs.DataAccessOperation.Mutate, store.CapturedOperation);
        Assert.NotNull(store.CapturedScope);
        Assert.False(store.CapturedScope!.IncludesAllTenants);
        Assert.Equal(TestJobActorProvider.TenantId, store.CapturedScope.TenantId);
    }

    [Fact]
    public async Task ListActionExecutesUnderReadOperationAndActorTenantScope()
    {
        var scopeProvider = ScopeProvider(JobPermissions.EmailRead);
        var store = new ScopeCapturingJobStore(scopeProvider);
        var action = new ListJobsAction(
            store,
            new TestJobActorProvider(new JobActor(
                TestJobActorProvider.ActorId,
                TestJobActorProvider.TenantId,
                [JobPermissions.EmailRead])),
            new JobAuthorizationRuleEvaluator(),
            scopeProvider);

        _ = await action.ExecuteAsync(new ListJobsActionRequest());

        Assert.Equal(global::JobSchedulerPrototype.Jobs.DataAccessOperation.Read, store.CapturedOperation);
        Assert.NotNull(store.CapturedScope);
        Assert.False(store.CapturedScope!.IncludesAllTenants);
        Assert.Equal(TestJobActorProvider.TenantId, store.CapturedScope.TenantId);
    }

    [Fact]
    public async Task ListActionExecutesUnderReadOperationAndAllTenantsScopeForGlobalReader()
    {
        var scopeProvider = ScopeProvider(
            JobPermissions.EmailRead,
            JobPermissions.GlobalRead);
        var store = new ScopeCapturingJobStore(scopeProvider);
        var action = new ListJobsAction(
            store,
            new TestJobActorProvider(new JobActor(
                TestJobActorProvider.ActorId,
                TestJobActorProvider.TenantId,
                [JobPermissions.EmailRead, JobPermissions.GlobalRead])),
            new JobAuthorizationRuleEvaluator(),
            scopeProvider);

        _ = await action.ExecuteAsync(new ListJobsActionRequest());

        Assert.Equal(global::JobSchedulerPrototype.Jobs.DataAccessOperation.Read, store.CapturedOperation);
        Assert.NotNull(store.CapturedScope);
        Assert.True(store.CapturedScope!.IncludesAllTenants);
    }

    [Fact]
    public async Task GetByIdActionExecutesUnderReadOperationAndAllTenantsScopeForGlobalReader()
    {
        var scopeProvider = ScopeProvider(
            JobPermissions.EmailRead,
            JobPermissions.GlobalRead);
        var store = new ScopeCapturingJobStore(scopeProvider);
        var action = new GetJobByIdAction(
            store,
            new TestJobActorProvider(new JobActor(
                TestJobActorProvider.ActorId,
                TestJobActorProvider.TenantId,
                [JobPermissions.EmailRead, JobPermissions.GlobalRead])),
            new JobAuthorizationRuleEvaluator(),
            scopeProvider);

        _ = await action.ExecuteAsync(new GetJobByIdActionRequest(Guid.NewGuid()));

        Assert.Equal(global::JobSchedulerPrototype.Jobs.DataAccessOperation.Read, store.CapturedOperation);
        Assert.NotNull(store.CapturedScope);
        Assert.True(store.CapturedScope!.IncludesAllTenants);
    }

    [Fact]
    public async Task ListActionExecutesUnderReadOperationAndAllTenantsScopeForWildcardPermission()
    {
        var scopeProvider = ScopeProvider(JobPermissions.All);
        var store = new ScopeCapturingJobStore(scopeProvider);
        var action = new ListJobsAction(
            store,
            new TestJobActorProvider(new JobActor(
                TestJobActorProvider.ActorId,
                TestJobActorProvider.TenantId,
                [JobPermissions.All])),
            new JobAuthorizationRuleEvaluator(),
            scopeProvider);

        _ = await action.ExecuteAsync(new ListJobsActionRequest());

        Assert.Equal(global::JobSchedulerPrototype.Jobs.DataAccessOperation.Read, store.CapturedOperation);
        Assert.NotNull(store.CapturedScope);
        Assert.True(store.CapturedScope!.IncludesAllTenants);
    }

    [Fact]
    public async Task ClaimActionExecutesUnderMutateOperationAndAllTenantsScope()
    {
        var scopeProvider = ScopeProvider(JobPermissions.Execute);
        var lifecycle = new ScopeCapturingLifecycleService(scopeProvider);
        var action = new ClaimNextDueJobAction(
            lifecycle,
            new TestJobActorProvider(),
            new JobAuthorizationRuleEvaluator(),
            scopeProvider);

        var now = new DateTimeOffset(2026, 5, 8, 10, 0, 0, TimeSpan.Zero);
        _ = await action.ExecuteAsync(new ClaimNextDueJobActionRequest(
            now,
            "worker-1",
            now.AddMinutes(1)));

        Assert.Equal(global::JobSchedulerPrototype.Jobs.DataAccessOperation.Mutate, lifecycle.CapturedOperation);
        Assert.NotNull(lifecycle.CapturedScope);
        Assert.True(lifecycle.CapturedScope!.IncludesAllTenants);
    }

    [Fact]
    public async Task AcknowledgeActionExecutesUnderMutateOperationAndActorTenantScope()
    {
        var scopeProvider = ScopeProvider(
            JobPermissions.EmailRead,
            JobPermissions.EmailManage);
        var store = new ScopeCapturingJobStore(scopeProvider);
        var action = new AcknowledgeJobAction(
            store,
            new TestJobActorProvider(new JobActor(
                TestJobActorProvider.ActorId,
                TestJobActorProvider.TenantId,
                [JobPermissions.EmailRead, JobPermissions.EmailManage])),
            new JobAuthorizationRuleEvaluator(),
            scopeProvider);

        _ = await action.ExecuteAsync(new AcknowledgeJobActionRequest(Guid.NewGuid()));

        Assert.Equal(global::JobSchedulerPrototype.Jobs.DataAccessOperation.Mutate, store.CapturedOperation);
        Assert.NotNull(store.CapturedScope);
        Assert.False(store.CapturedScope!.IncludesAllTenants);
        Assert.Equal(TestJobActorProvider.TenantId, store.CapturedScope.TenantId);
    }

    [Fact]
    public async Task AcknowledgeActionExecutesUnderMutateOperationAndAllTenantsScopeForGlobalManager()
    {
        var scopeProvider = ScopeProvider(
            JobPermissions.EmailRead,
            JobPermissions.EmailManage,
            JobPermissions.GlobalRead);
        var store = new ScopeCapturingJobStore(scopeProvider);
        var action = new AcknowledgeJobAction(
            store,
            new TestJobActorProvider(new JobActor(
                TestJobActorProvider.ActorId,
                TestJobActorProvider.TenantId,
                [JobPermissions.EmailRead, JobPermissions.EmailManage, JobPermissions.GlobalRead])),
            new JobAuthorizationRuleEvaluator(),
            scopeProvider);

        _ = await action.ExecuteAsync(new AcknowledgeJobActionRequest(Guid.NewGuid()));

        Assert.Equal(global::JobSchedulerPrototype.Jobs.DataAccessOperation.Mutate, store.CapturedOperation);
        Assert.NotNull(store.CapturedScope);
        Assert.True(store.CapturedScope!.IncludesAllTenants);
    }

    private static IDataAccessScopeProvider ScopeProvider(params string[] permissions)
    {
        return new FixedDataAccessScopeProvider(
            DataAccessScope.Tenant(TestJobActorProvider.TenantId),
            new JobActor(
                TestJobActorProvider.ActorId,
                TestJobActorProvider.TenantId,
                permissions));
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse("""{"userId":"user_123","email":"person@example.com"}""");
        return document.RootElement.Clone();
    }

    private sealed class ScopeCapturingJobStore : IJobStore
    {
        private readonly IDataAccessScopeProvider _scopeProvider;

        public ScopeCapturingJobStore(IDataAccessScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }

        public DataAccessScope? CapturedScope { get; private set; }
        public global::JobSchedulerPrototype.Jobs.DataAccessOperation? CapturedOperation { get; private set; }

        public void Add(JobRecord job)
        {
            Capture();
        }

        public JobRecord? Get(Guid id)
        {
            Capture();
            return null;
        }

        public IReadOnlyCollection<JobRecord> List()
        {
            Capture();
            return [];
        }

        public JobRecord? TryClaimNextDueJob(DateTimeOffset now, string workerId, DateTimeOffset leaseExpiresAt)
        {
            Capture();
            return null;
        }

        public bool RenewLease(
            Guid id,
            Guid expectedCurrentStateChangeId,
            DateTimeOffset renewedAt,
            DateTimeOffset leaseExpiresAt)
        {
            Capture();
            return false;
        }

        public bool MarkCompleted(Guid id, Guid expectedCurrentStateChangeId)
        {
            Capture();
            return false;
        }

        public bool MarkFailed(Guid id, Guid expectedCurrentStateChangeId, string reason)
        {
            Capture();
            return false;
        }

        public bool ScheduleRetry(
            Guid id,
            Guid expectedCurrentStateChangeId,
            string reason,
            DateTimeOffset scheduledAt)
        {
            Capture();
            return false;
        }

        public bool Acknowledge(
            Guid id,
            string acknowledgedBy,
            DateTimeOffset acknowledgedAt)
        {
            Capture();
            return true;
        }

        private void Capture()
        {
            CapturedScope = _scopeProvider.Current;
            CapturedOperation = _scopeProvider.CurrentOperation;
        }
    }

    private sealed class ScopeCapturingLifecycleService : IJobLifecycleService
    {
        private readonly IDataAccessScopeProvider _scopeProvider;

        public ScopeCapturingLifecycleService(IDataAccessScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }

        public DataAccessScope? CapturedScope { get; private set; }
        public global::JobSchedulerPrototype.Jobs.DataAccessOperation? CapturedOperation { get; private set; }

        public JobEnqueueResult Enqueue(string type, JsonElement payload, int? delaySeconds)
        {
            Capture();
            return JobEnqueueResult.Rejected("Not needed for this test.");
        }

        public JobRecord? ClaimNextDueJob(DateTimeOffset now, string workerId, DateTimeOffset leaseExpiresAt)
        {
            Capture();
            return null;
        }

        public bool RenewLease(JobRecord job, DateTimeOffset renewedAt, DateTimeOffset leaseExpiresAt)
        {
            Capture();
            return false;
        }

        public JobExecutionCompletion CompleteExecution(JobRecord job, JobExecutionResult result)
        {
            Capture();
            return JobExecutionCompletion.LeaseLost();
        }

        private void Capture()
        {
            CapturedScope = _scopeProvider.Current;
            CapturedOperation = _scopeProvider.CurrentOperation;
        }
    }
}
