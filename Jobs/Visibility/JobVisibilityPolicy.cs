namespace JobSchedulerPrototype.Jobs;

public sealed class JobVisibilityPolicy : DataVisibilityPolicy<JobRecord>
{
    public override IReadOnlyList<IDataVisibilityRule<JobRecord>> Rules { get; } =
    [
        new TenantVisibilityRule<JobRecord>()
    ];
}
