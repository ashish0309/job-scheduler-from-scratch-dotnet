namespace JobSchedulerPrototype.Jobs;

public sealed class JobDataAccessPolicy : DataAccessPolicy<JobRecord>
{
    public override IReadOnlyList<IDataAccessRule<JobRecord>> CommonRules { get; } =
    [
        new TenantBoundaryRule<JobRecord>()
    ];

    public override IReadOnlyDictionary<DataAccessOperation, IReadOnlyList<IDataAccessRule<JobRecord>>> RulesByOperation { get; } =
        new Dictionary<DataAccessOperation, IReadOnlyList<IDataAccessRule<JobRecord>>>
        {
            [DataAccessOperation.Read] =
            [
                new OwnerGrantRule<JobRecord>(),
                new EmailManageGrantRule<JobRecord>(),
                new GlobalReadGrantRule<JobRecord>()
            ],
            [DataAccessOperation.Mutate] = []
        };
}
