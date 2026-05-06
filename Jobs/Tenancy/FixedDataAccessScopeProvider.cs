namespace JobSchedulerPrototype.Jobs;

public sealed class FixedDataAccessScopeProvider : IDataAccessScopeProvider
{
    private static readonly JobActor DefaultActor =
        new("system", "system", [JobPermissions.All]);

    private readonly DataAccessScope _defaultScope;
    private readonly JobActor _actor;
    private readonly AsyncLocal<DataAccessScope?> _currentScope = new();

    public FixedDataAccessScopeProvider(DataAccessScope defaultScope, JobActor? actor = null)
    {
        _defaultScope = defaultScope;
        _actor = actor ?? DefaultActor;
    }

    public JobActor CurrentActor => _actor;

    public DataAccessScope Current => _currentScope.Value ?? _defaultScope;

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
