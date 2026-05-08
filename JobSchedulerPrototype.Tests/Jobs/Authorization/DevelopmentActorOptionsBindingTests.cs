using JobSchedulerPrototype.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class DevelopmentActorOptionsBindingTests
{
    [Fact]
    public void BindingConfiguredPermissionsDoesNotRetainWildcardPermission()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JobScheduler:DevelopmentActor:Permissions:0"] = JobPermissions.EmailRead,
                ["JobScheduler:DevelopmentActor:Permissions:1"] = JobPermissions.EmailManage
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<DevelopmentActorOptions>(
            configuration.GetSection(DevelopmentActorOptions.SectionName));

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider
            .GetRequiredService<IOptions<DevelopmentActorOptions>>()
            .Value;

        Assert.Equal(
            [JobPermissions.EmailRead, JobPermissions.EmailManage],
            options.Permissions);
        Assert.DoesNotContain(
            JobPermissions.All,
            options.Permissions,
            StringComparer.OrdinalIgnoreCase);
    }
}
