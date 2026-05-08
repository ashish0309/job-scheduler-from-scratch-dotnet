using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

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
    private readonly IOptions<DevelopmentActorOptions> _options;

    public DevelopmentHeaderJobActorProvider(
        IHttpContextAccessor httpContextAccessor,
        IOptions<DevelopmentActorOptions> options)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options;
    }

    public JobActor GetCurrentActor()
    {
        var configuredActor = ConfiguredActor();
        if (!_options.Value.AllowRequestHeaders)
        {
            return configuredActor;
        }

        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is null)
        {
            return configuredActor;
        }

        return new JobActor(
            ReadHeaderOrDefault(request, ActorIdHeaderName, configuredActor.Id),
            ReadHeaderOrDefault(request, TenantIdHeaderName, configuredActor.TenantId),
            ReadPermissionsOrDefault(request, configuredActor.Permissions));
    }

    private JobActor ConfiguredActor()
    {
        var configuredPermissions = _options.Value.Permissions is null
            || _options.Value.Permissions.Length == 0
            ? DefaultPermissions
            : _options.Value.Permissions;

        return new JobActor(
            string.IsNullOrWhiteSpace(_options.Value.ActorId)
                ? DefaultActorId
                : _options.Value.ActorId.Trim(),
            string.IsNullOrWhiteSpace(_options.Value.TenantId)
                ? DefaultTenantId
                : _options.Value.TenantId.Trim(),
            configuredPermissions);
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

    private static IEnumerable<string> ReadPermissionsOrDefault(
        HttpRequest request,
        IEnumerable<string> defaultPermissions)
    {
        var permissions = request.Headers[PermissionsHeaderName].ToString();
        if (string.IsNullOrWhiteSpace(permissions))
        {
            return defaultPermissions;
        }

        return permissions.Split(
            ',',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
}
