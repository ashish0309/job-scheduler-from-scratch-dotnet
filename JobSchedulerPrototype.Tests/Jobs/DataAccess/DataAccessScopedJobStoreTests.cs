using System.Text.Json;
using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class DataAccessScopedJobStoreTests
{
    [Fact]
    public void IJobStoreMethodsSetExpectedDataAccessOperations()
    {
        var actorProvider = new TestJobActorProvider();
        var scopeProvider = new DataAccessScopeProvider(actorProvider);
        var innerStore = new TrackingJobStore(scopeProvider);
        var scopedStore = new DataAccessScopedJobStore(innerStore, scopeProvider);

        using (scopeProvider.BeginScope(DataAccessScope.AllTenants()))
        {
            scopedStore.Add(CreateQueuedJob());
            _ = scopedStore.Get(Guid.NewGuid());
            _ = scopedStore.List();
            _ = scopedStore.TryClaimNextDueJob(
                DateTimeOffset.UtcNow,
                "worker-1",
                DateTimeOffset.UtcNow.AddMinutes(1));
            _ = scopedStore.RenewLease(
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(1));
            _ = scopedStore.MarkCompleted(Guid.NewGuid(), Guid.NewGuid());
            _ = scopedStore.MarkFailed(Guid.NewGuid(), Guid.NewGuid(), "failed");
            _ = scopedStore.ScheduleRetry(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "retry",
                DateTimeOffset.UtcNow.AddMinutes(1));
        }

        var operations = innerStore.CapturedOperations;
        Assert.Equal(8, operations.Count);
        Assert.Equal(DataAccessOperation.Mutate, operations[0]);
        Assert.Equal(DataAccessOperation.Read, operations[1]);
        Assert.Equal(DataAccessOperation.Read, operations[2]);
        Assert.Equal(DataAccessOperation.Mutate, operations[3]);
        Assert.Equal(DataAccessOperation.Mutate, operations[4]);
        Assert.Equal(DataAccessOperation.Mutate, operations[5]);
        Assert.Equal(DataAccessOperation.Mutate, operations[6]);
        Assert.Equal(DataAccessOperation.Mutate, operations[7]);

        Assert.All(innerStore.CapturedScopes, scope => Assert.True(scope.IncludesAllTenants));
    }

    private static JobRecord CreateQueuedJob()
    {
        return JobRecord.Enqueue(
            Guid.NewGuid(),
            TestJobActorProvider.TenantId,
            TestJobActorProvider.ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 1,
            DateTimeOffset.UtcNow);
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse("{\"userId\":\"user-123\",\"email\":\"person@example.com\"}");
        return document.RootElement.Clone();
    }

    private sealed class TrackingJobStore : IJobStore
    {
        private readonly IDataAccessScopeProvider _scopeProvider;

        public TrackingJobStore(IDataAccessScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }

        public List<DataAccessOperation> CapturedOperations { get; } = [];
        public List<DataAccessScope> CapturedScopes { get; } = [];

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

        public JobRecord? TryClaimNextDueJob(
            DateTimeOffset now,
            string workerId,
            DateTimeOffset leaseExpiresAt)
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

        private void Capture()
        {
            CapturedOperations.Add(_scopeProvider.CurrentOperation);
            CapturedScopes.Add(_scopeProvider.Current);
        }
    }
}
