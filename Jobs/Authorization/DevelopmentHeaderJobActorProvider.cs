using Microsoft.AspNetCore.Http;

namespace JobSchedulerPrototype.Jobs;

public sealed class DevelopmentHeaderJobActorProvider : IJobActorProvider
{
    public const string ActorIdHeaderName = "X-Actor-Id";
    public const string TenantIdHeaderName = "X-Tenant-Id";
    public const string PermissionsHeaderName = "X-Permissions";

    public const string DefaultActorId = "dev-user";
    public const string DefaultTenantId = "dev-tenant";

    private static readonly string[] DefaultPermissions = [JobPermissions.All];

    private readonly IHttpContextAccessor _httpContextAccessor;

    public DevelopmentHeaderJobActorProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public JobActor GetCurrentActor()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is null)
        {
            return DefaultActor();
        }

        return new JobActor(
            ReadHeaderOrDefault(request, ActorIdHeaderName, DefaultActorId),
            ReadHeaderOrDefault(request, TenantIdHeaderName, DefaultTenantId),
            ReadPermissionsOrDefault(request));
    }

    private static JobActor DefaultActor()
    {
        return new JobActor(
            DefaultActorId,
            DefaultTenantId,
            DefaultPermissions);
    }

    private static string ReadHeaderOrDefault(
        HttpRequest request,
        string headerName,
        string defaultValue)
    {
        var value = request.Headers[headerName].ToString();

        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value.Trim();
    }

    private static IEnumerable<string> ReadPermissionsOrDefault(HttpRequest request)
    {
        var permissions = request.Headers[PermissionsHeaderName].ToString();
        if (string.IsNullOrWhiteSpace(permissions))
        {
            return DefaultPermissions;
        }

        return permissions.Split(
            ',',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
}
