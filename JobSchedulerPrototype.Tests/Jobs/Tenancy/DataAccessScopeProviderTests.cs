using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class DataAccessScopeProviderTests
{
    [Fact]
    public void CurrentDefaultsToCurrentActorTenant()
    {
        var provider = new DataAccessScopeProvider(new TestJobActorProvider());

        var scope = provider.Current;

        Assert.Equal(TestJobActorProvider.ActorId, provider.CurrentActor.Id);
        Assert.False(scope.IncludesAllTenants);
        Assert.Equal(TestJobActorProvider.TenantId, scope.TenantId);
    }

    [Fact]
    public void BeginScopeTemporarilyOverridesCurrentScope()
    {
        var provider = new DataAccessScopeProvider(new TestJobActorProvider());

        using (provider.BeginScope(DataAccessScope.AllTenants()))
        {
            Assert.True(provider.Current.IncludesAllTenants);
            Assert.Null(provider.Current.TenantId);
        }

        Assert.False(provider.Current.IncludesAllTenants);
        Assert.Equal(TestJobActorProvider.TenantId, provider.Current.TenantId);
    }
}
