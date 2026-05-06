namespace JobSchedulerPrototype.Jobs;

public interface IDataAccessPolicyContext
{
    JobActor Actor { get; }

    DataAccessScope Scope { get; }

    DataAccessOperation Operation { get; }

    bool IncludesAllTenants => Scope.IncludesAllTenants;

    string? CurrentTenantId => Scope.TenantId;
}
