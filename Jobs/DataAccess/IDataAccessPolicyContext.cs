namespace JobSchedulerPrototype.Jobs;

public interface IDataAccessPolicyContext
{
    JobActor Actor { get; }

    string CurrentActorId => Actor.Id;

    DataAccessScope Scope { get; }

    DataAccessOperation Operation { get; }

    bool IncludesAllTenants => Scope.IncludesAllTenants;

    string? CurrentTenantId => Scope.TenantId;

    bool CanManageEmailJobs => Actor.HasPermission(JobPermissions.EmailManage);

    bool CanReadAllTenantsJobs => Actor.Permissions.Contains(JobPermissions.GlobalRead);
}
