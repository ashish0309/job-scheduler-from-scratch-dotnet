using JobSchedulerPrototype.Jobs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class DevelopmentHeaderJobActorProviderTests
{
    [Fact]
    public void GetCurrentActorUsesDevelopmentActorWhenHeadersAreMissing()
    {
        var provider = CreateProvider(new DefaultHttpContext());

        var actor = provider.GetCurrentActor();

        Assert.Equal(DevelopmentHeaderJobActorProvider.DefaultActorId, actor.Id);
        Assert.Equal(DevelopmentHeaderJobActorProvider.DefaultTenantId, actor.TenantId);
        Assert.True(actor.HasPermission(JobPermissions.EmailRead));
        Assert.Contains(JobPermissions.All, actor.Permissions);
    }

    [Fact]
    public void GetCurrentActorReadsActorFromHeaders()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[DevelopmentHeaderJobActorProvider.ActorIdHeaderName] = " user-123 ";
        httpContext.Request.Headers[DevelopmentHeaderJobActorProvider.TenantIdHeaderName] = " tenant-alpha ";
        httpContext.Request.Headers[DevelopmentHeaderJobActorProvider.PermissionsHeaderName] =
            "jobs.email.read, jobs.email.enqueue";
        var provider = CreateProvider(httpContext);

        var actor = provider.GetCurrentActor();

        Assert.Equal("user-123", actor.Id);
        Assert.Equal("tenant-alpha", actor.TenantId);
        Assert.True(actor.HasPermission(JobPermissions.EmailRead));
        Assert.True(actor.HasPermission(JobPermissions.EmailEnqueue));
        Assert.False(actor.HasPermission(JobPermissions.EmailManage));
    }

    [Fact]
    public void GetCurrentActorUsesDevelopmentActorWhenHttpContextIsMissing()
    {
        var provider = new DevelopmentHeaderJobActorProvider(
            new HttpContextAccessor(),
            CreateOptions());

        var actor = provider.GetCurrentActor();

        Assert.Equal(DevelopmentHeaderJobActorProvider.DefaultActorId, actor.Id);
        Assert.Equal(DevelopmentHeaderJobActorProvider.DefaultTenantId, actor.TenantId);
        Assert.True(actor.HasPermission(JobPermissions.Execute));
    }

    [Fact]
    public void GetCurrentActorIgnoresHeadersWhenHeaderUsageIsDisabled()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[DevelopmentHeaderJobActorProvider.ActorIdHeaderName] = "owner-alpha";
        httpContext.Request.Headers[DevelopmentHeaderJobActorProvider.TenantIdHeaderName] = "tenant-alpha";
        httpContext.Request.Headers[DevelopmentHeaderJobActorProvider.PermissionsHeaderName] = "jobs.email.read";
        var options = CreateOptions(new DevelopmentActorOptions
        {
            AllowRequestHeaders = false,
            ActorId = "configured-user",
            TenantId = "configured-tenant",
            Permissions = [JobPermissions.EmailManage]
        });
        var provider = CreateProvider(httpContext, options);

        var actor = provider.GetCurrentActor();

        Assert.Equal("configured-user", actor.Id);
        Assert.Equal("configured-tenant", actor.TenantId);
        Assert.True(actor.HasPermission(JobPermissions.EmailManage));
        Assert.False(actor.HasPermission(JobPermissions.EmailRead));
    }

    private static DevelopmentHeaderJobActorProvider CreateProvider(
        HttpContext httpContext,
        IOptions<DevelopmentActorOptions>? options = null)
    {
        return new DevelopmentHeaderJobActorProvider(
            new HttpContextAccessor
            {
                HttpContext = httpContext
            },
            options ?? CreateOptions());
    }

    private static IOptions<DevelopmentActorOptions> CreateOptions(
        DevelopmentActorOptions? options = null)
    {
        return Options.Create(options ?? new DevelopmentActorOptions());
    }
}
