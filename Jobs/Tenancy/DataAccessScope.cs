namespace JobSchedulerPrototype.Jobs;

public sealed record DataAccessScope
{
    private DataAccessScope(string? tenantId, bool includesAllTenants)
    {
        TenantId = tenantId;
        IncludesAllTenants = includesAllTenants;
    }

    public string? TenantId { get; }

    public bool IncludesAllTenants { get; }

    public static DataAccessScope Tenant(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));
        }

        return new DataAccessScope(tenantId.Trim(), includesAllTenants: false);
    }

    public static DataAccessScope AllTenants()
    {
        return new DataAccessScope(tenantId: null, includesAllTenants: true);
    }
}
