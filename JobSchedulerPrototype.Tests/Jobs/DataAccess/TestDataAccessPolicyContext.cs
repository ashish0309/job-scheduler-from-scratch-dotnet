using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

internal sealed class TestDataAccessPolicyContext : IDataAccessPolicyContext
{
    public TestDataAccessPolicyContext(
        DataAccessScope scope,
        DataAccessOperation operation = DataAccessOperation.Read,
        JobActor? actor = null)
    {
        Scope = scope;
        Operation = operation;
        Actor = actor
            ?? new JobActor(
                TestJobActorProvider.ActorId,
                TestJobActorProvider.TenantId,
                [JobPermissions.All]);
    }

    public JobActor Actor { get; }

    public DataAccessScope Scope { get; }

    public DataAccessOperation Operation { get; }
}
