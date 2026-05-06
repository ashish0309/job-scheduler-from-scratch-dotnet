using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

internal sealed class TestDataAccessPolicyContext : IDataAccessPolicyContext
{
    public TestDataAccessPolicyContext(
        DataAccessScope scope,
        DataAccessOperation operation = DataAccessOperation.Read)
    {
        Scope = scope;
        Operation = operation;
    }

    public JobActor Actor { get; } =
        new(TestJobActorProvider.ActorId, TestJobActorProvider.TenantId, [JobPermissions.All]);

    public DataAccessScope Scope { get; }

    public DataAccessOperation Operation { get; }
}
