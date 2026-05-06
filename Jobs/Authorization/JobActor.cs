namespace JobSchedulerPrototype.Jobs;

public sealed record JobActor
{
    public JobActor(
        string id,
        string tenantId,
        IEnumerable<string> permissions)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Actor ID is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));
        }

        Id = id;
        TenantId = tenantId;
        Permissions = permissions
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Select(permission => permission.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public string Id { get; }

    public string TenantId { get; }

    public IReadOnlySet<string> Permissions { get; }

    public bool HasPermission(string permission)
    {
        return Permissions.Contains(JobPermissions.All)
            || Permissions.Contains(permission);
    }
}
