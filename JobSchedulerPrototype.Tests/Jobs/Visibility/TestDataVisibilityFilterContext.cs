using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

internal sealed class TestDataVisibilityFilterContext : IDataVisibilityFilterContext
{
    public TestDataVisibilityFilterContext(DataAccessScope scope)
    {
        Scope = scope;
    }

    public JobActor Actor { get; } =
        new(TestJobActorProvider.ActorId, TestJobActorProvider.TenantId, [JobPermissions.All]);

    public DataAccessScope Scope { get; }
}
