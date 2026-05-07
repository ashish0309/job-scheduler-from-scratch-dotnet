namespace JobSchedulerPrototype.Jobs;

public sealed class DataAccessScopedJobStore : IJobStore
{
    private readonly IJobStore _inner;
    private readonly IDataAccessScopeProvider _scopeProvider;

    public DataAccessScopedJobStore(
        IJobStore inner,
        IDataAccessScopeProvider scopeProvider)
    {
        _inner = inner;
        _scopeProvider = scopeProvider;
    }

    public void Add(JobRecord job)
    {
        using var scope = BeginOperation(DataAccessOperation.Mutate);
        _inner.Add(job);
    }

    public JobRecord? Get(Guid id)
    {
        using var scope = BeginOperation(DataAccessOperation.Read);
        return _inner.Get(id);
    }

    public IReadOnlyCollection<JobRecord> List()
    {
        using var scope = BeginOperation(DataAccessOperation.Read);
        return _inner.List();
    }

    public JobRecord? TryClaimNextDueJob(
        DateTimeOffset now,
        string workerId,
        DateTimeOffset leaseExpiresAt)
    {
        using var scope = BeginOperation(DataAccessOperation.Mutate);
        return _inner.TryClaimNextDueJob(now, workerId, leaseExpiresAt);
    }

    public bool RenewLease(
        Guid id,
        Guid expectedCurrentStateChangeId,
        DateTimeOffset renewedAt,
        DateTimeOffset leaseExpiresAt)
    {
        using var scope = BeginOperation(DataAccessOperation.Mutate);
        return _inner.RenewLease(
            id,
            expectedCurrentStateChangeId,
            renewedAt,
            leaseExpiresAt);
    }

    public bool MarkCompleted(Guid id, Guid expectedCurrentStateChangeId)
    {
        using var scope = BeginOperation(DataAccessOperation.Mutate);
        return _inner.MarkCompleted(id, expectedCurrentStateChangeId);
    }

    public bool MarkFailed(
        Guid id,
        Guid expectedCurrentStateChangeId,
        string reason)
    {
        using var scope = BeginOperation(DataAccessOperation.Mutate);
        return _inner.MarkFailed(id, expectedCurrentStateChangeId, reason);
    }

    public bool ScheduleRetry(
        Guid id,
        Guid expectedCurrentStateChangeId,
        string reason,
        DateTimeOffset scheduledAt)
    {
        using var scope = BeginOperation(DataAccessOperation.Mutate);
        return _inner.ScheduleRetry(
            id,
            expectedCurrentStateChangeId,
            reason,
            scheduledAt);
    }

    private IDisposable BeginOperation(DataAccessOperation operation)
    {
        var scope = _scopeProvider.Current;
        return _scopeProvider.BeginScope(scope, operation);
    }
}
