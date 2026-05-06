namespace JobSchedulerPrototype.Jobs;

public interface IDataAccessScopeProvider
{
    JobActor CurrentActor { get; }

    DataAccessScope Current { get; }

    IDisposable BeginScope(DataAccessScope scope);
}
