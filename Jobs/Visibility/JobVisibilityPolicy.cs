namespace JobSchedulerPrototype.Jobs;

public sealed class JobVisibilityPolicy : DataVisibilityPolicy<JobRecord>
{
    public override IReadOnlyList<IDataVisibilityRule<JobRecord>> CommonRules { get; } =
    [
        new TenantVisibilityRule<JobRecord>()
    ];

    public override IReadOnlyDictionary<DataAccessOperation, IReadOnlyList<IDataVisibilityRule<JobRecord>>> RulesByOperation { get; } =
        new Dictionary<DataAccessOperation, IReadOnlyList<IDataVisibilityRule<JobRecord>>>
        {
            [DataAccessOperation.Read] = []
        };
}
