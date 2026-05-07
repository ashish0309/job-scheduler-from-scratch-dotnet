namespace JobSchedulerPrototype.Jobs;

public sealed class DataAccessScopeProvider : IDataAccessScopeProvider
{
    private readonly IJobActorProvider _actorProvider;
    private readonly AsyncLocal<DataAccessScope?> _currentScope = new();
    private readonly AsyncLocal<DataAccessOperation?> _currentOperation = new();

    public DataAccessScopeProvider(IJobActorProvider actorProvider)
    {
        _actorProvider = actorProvider;
    }

    public JobActor CurrentActor => _actorProvider.GetCurrentActor();

    public DataAccessScope Current =>
        _currentScope.Value ?? DataAccessScope.Tenant(CurrentActor.TenantId);

    public DataAccessOperation CurrentOperation =>
        _currentOperation.Value ?? DataAccessOperation.Read;

    public IDisposable BeginScope(DataAccessScope scope, DataAccessOperation operation)
    {
        var previousScope = _currentScope.Value;
        var previousOperation = _currentOperation.Value;
        _currentScope.Value = scope;
        _currentOperation.Value = operation;

        return new ScopeHandle(() =>
        {
            _currentScope.Value = previousScope;
            _currentOperation.Value = previousOperation;
        });
    }

    public IDisposable BeginScope(DataAccessScope scope)
    {
        return BeginScope(scope, DataAccessOperation.Read);
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
