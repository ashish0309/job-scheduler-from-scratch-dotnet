namespace JobSchedulerPrototype.Jobs;

public sealed class FixedDataAccessScopeProvider : IDataAccessScopeProvider
{
    private static readonly JobActor DefaultActor =
        new("system", "system", [JobPermissions.All]);

    private readonly DataAccessScope _defaultScope;
    private readonly JobActor _actor;
    private readonly DataAccessOperation _defaultOperation;
    private readonly AsyncLocal<DataAccessScope?> _currentScope = new();
    private readonly AsyncLocal<DataAccessOperation?> _currentOperation = new();

    public FixedDataAccessScopeProvider(
        DataAccessScope defaultScope,
        JobActor? actor = null,
        DataAccessOperation defaultOperation = DataAccessOperation.Read)
    {
        _defaultScope = defaultScope;
        _actor = actor ?? DefaultActor;
        _defaultOperation = defaultOperation;
    }

    public JobActor CurrentActor => _actor;

    public DataAccessScope Current => _currentScope.Value ?? _defaultScope;

    public DataAccessOperation CurrentOperation => _currentOperation.Value ?? _defaultOperation;

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
