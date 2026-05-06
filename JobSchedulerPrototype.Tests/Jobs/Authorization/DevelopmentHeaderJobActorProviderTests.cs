using JobSchedulerPrototype.Jobs;
using Microsoft.AspNetCore.Http;

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
        var provider = new DevelopmentHeaderJobActorProvider(new HttpContextAccessor());

        var actor = provider.GetCurrentActor();

        Assert.Equal(DevelopmentHeaderJobActorProvider.DefaultActorId, actor.Id);
        Assert.Equal(DevelopmentHeaderJobActorProvider.DefaultTenantId, actor.TenantId);
        Assert.True(actor.HasPermission(JobPermissions.Execute));
    }

    private static DevelopmentHeaderJobActorProvider CreateProvider(HttpContext httpContext)
    {
        return new DevelopmentHeaderJobActorProvider(new HttpContextAccessor
        {
            HttpContext = httpContext
        });
    }
}
