namespace JobSchedulerPrototype.Jobs;

public sealed class ListJobsAction : JobAuthorizedAction<ListJobsActionRequest, ListJobsActionResult>
{
    private readonly IJobStore _jobs;

    public ListJobsAction(
        IJobStore jobs,
        IJobActorProvider actorProvider,
        IJobAuthorizationRuleEvaluator ruleEvaluator)
        : base(actorProvider, ruleEvaluator)
    {
        _jobs = jobs;
    }

    protected override IReadOnlyList<IJobAuthorizationRule> BuildAuthorizationRules(ListJobsActionRequest request)
    {
        return
        [
            new PermissionJobAuthorizationRule(
                JobPermissions.EmailRead,
                "Actor is not authorized to read jobs.")
        ];
    }

    protected override ListJobsActionResult OnAuthorizationDenied(JobAuthorizationResult result)
    {
        return ListJobsActionResult.Denied(
            result.ErrorMessage ?? "Actor is not authorized to read jobs.");
    }

    protected override Task<ListJobsActionResult> ExecuteAuthorizedAsync(
        ListJobsActionRequest request,
        JobActor actor,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ListJobsActionResult.Authorized(_jobs.List()));
    }
}

public sealed record ListJobsActionRequest : IJobActionRequest<ListJobsActionResult>;

public sealed record ListJobsActionResult(
    bool IsAuthorized,
    IReadOnlyCollection<JobRecord> Jobs,
    string? ErrorMessage)
{
    public static ListJobsActionResult Authorized(IReadOnlyCollection<JobRecord> jobs)
    {
        return new ListJobsActionResult(
            true,
            jobs,
            ErrorMessage: null);
    }

    public static ListJobsActionResult Denied(string errorMessage)
    {
        return new ListJobsActionResult(
            false,
            Array.Empty<JobRecord>(),
            errorMessage);
    }
}
