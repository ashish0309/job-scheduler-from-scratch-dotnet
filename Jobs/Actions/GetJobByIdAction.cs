namespace JobSchedulerPrototype.Jobs;

public sealed class GetJobByIdAction : JobAuthorizedAction<GetJobByIdActionRequest, GetJobByIdActionResult>
{
    private readonly IJobStore _jobs;

    public GetJobByIdAction(
        IJobStore jobs,
        IJobActorProvider actorProvider,
        IJobAuthorizationRuleEvaluator ruleEvaluator,
        IDataAccessScopeProvider dataAccessScopeProvider)
        : base(actorProvider, ruleEvaluator, dataAccessScopeProvider)
    {
        _jobs = jobs;
    }

    protected override IReadOnlyList<IJobAuthorizationRule> BuildAuthorizationRules(GetJobByIdActionRequest request)
    {
        return
        [
            new PermissionJobAuthorizationRule(
                JobPermissions.EmailRead,
                "Actor is not authorized to read jobs.")
        ];
    }

    protected override DataAccessScope BuildDataAccessScope(
        GetJobByIdActionRequest request,
        JobActor actor)
    {
        return actor.HasPermission(JobPermissions.GlobalRead)
            ? DataAccessScope.AllTenants()
            : DataAccessScope.Tenant(actor.TenantId);
    }

    protected override GetJobByIdActionResult OnAuthorizationDenied(JobAuthorizationResult result)
    {
        return GetJobByIdActionResult.Denied(
            result.ErrorMessage ?? "Actor is not authorized to read jobs.");
    }

    protected override Task<GetJobByIdActionResult> ExecuteAuthorizedAsync(
        GetJobByIdActionRequest request,
        JobActor actor,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(GetJobByIdActionResult.Authorized(_jobs.Get(request.Id)));
    }
}

public sealed record GetJobByIdActionRequest(Guid Id) : IJobActionRequest<GetJobByIdActionResult>;

public sealed record GetJobByIdActionResult(
    bool IsAuthorized,
    JobRecord? Job,
    string? ErrorMessage)
{
    public static GetJobByIdActionResult Authorized(JobRecord? job)
    {
        return new GetJobByIdActionResult(
            true,
            job,
            ErrorMessage: null);
    }

    public static GetJobByIdActionResult Denied(string errorMessage)
    {
        return new GetJobByIdActionResult(
            false,
            Job: null,
            errorMessage);
    }
}
