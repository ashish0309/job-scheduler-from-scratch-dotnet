namespace JobSchedulerPrototype.Jobs;

public interface IDataAccessScopeProvider
{
    JobActor? ScopedActor { get; }

    JobActor CurrentActor { get; }

    DataAccessScope Current { get; }

    DataAccessOperation CurrentOperation { get; }

    IDisposable BeginScope(DataAccessScope scope, DataAccessOperation operation);

    IDisposable BeginScope(DataAccessScope scope);

    IDisposable BeginActorScope(JobActor actor);
}
