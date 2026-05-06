namespace JobSchedulerPrototype.Jobs;

public sealed class DataAccessScopeProvider : IDataAccessScopeProvider
{
    private readonly IJobActorProvider _actorProvider;
    private readonly AsyncLocal<DataAccessScope?> _currentScope = new();

    public DataAccessScopeProvider(IJobActorProvider actorProvider)
    {
        _actorProvider = actorProvider;
    }

    public JobActor CurrentActor => _actorProvider.GetCurrentActor();

    public DataAccessScope Current =>
        _currentScope.Value ?? DataAccessScope.Tenant(CurrentActor.TenantId);

    public IDisposable BeginScope(DataAccessScope scope)
    {
        var previousScope = _currentScope.Value;
        _currentScope.Value = scope;

        return new ScopeHandle(() => _currentScope.Value = previousScope);
    }

    private sealed class ScopeHandle : IDisposable
    {
        private readonly Action _dispose;
        private bool _disposed;

        public ScopeHandle(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _dispose();
        }
    }
}
