namespace JobSchedulerPrototype.Jobs;

public sealed class ClaimNextDueJobAction : JobAuthorizedAction<ClaimNextDueJobActionRequest, ClaimNextDueJobActionResult>
{
    private readonly IJobLifecycleService _lifecycle;

    public ClaimNextDueJobAction(
        IJobLifecycleService lifecycle,
        IJobActorProvider actorProvider,
        IJobAuthorizationRuleEvaluator ruleEvaluator)
        : base(actorProvider, ruleEvaluator)
    {
        _lifecycle = lifecycle;
    }

    protected override IReadOnlyList<IJobAuthorizationRule> BuildAuthorizationRules(ClaimNextDueJobActionRequest request)
    {
        return
        [
            new PermissionJobAuthorizationRule(
                JobPermissions.Execute,
                "Actor is not authorized to claim jobs.")
        ];
    }

    protected override ClaimNextDueJobActionResult OnAuthorizationDenied(JobAuthorizationResult result)
    {
        return ClaimNextDueJobActionResult.Denied(
            result.ErrorMessage ?? "Actor is not authorized to claim jobs.");
    }

    protected override Task<ClaimNextDueJobActionResult> ExecuteAuthorizedAsync(
        ClaimNextDueJobActionRequest request,
        JobActor actor,
        CancellationToken cancellationToken)
    {
        var job = _lifecycle.ClaimNextDueJob(
            request.Now,
            request.WorkerId,
            request.LeaseExpiresAt);

        return Task.FromResult(ClaimNextDueJobActionResult.Authorized(job));
    }
}

public sealed record ClaimNextDueJobActionRequest(
    DateTimeOffset Now,
    string WorkerId,
    DateTimeOffset LeaseExpiresAt) : IJobActionRequest<ClaimNextDueJobActionResult>;

public sealed record ClaimNextDueJobActionResult(
    bool IsAuthorized,
    JobRecord? Job,
    string? ErrorMessage)
{
    public static ClaimNextDueJobActionResult Authorized(JobRecord? job)
    {
        return new ClaimNextDueJobActionResult(
            true,
            job,
            ErrorMessage: null);
    }

    public static ClaimNextDueJobActionResult Denied(string errorMessage)
    {
        return new ClaimNextDueJobActionResult(
            false,
            Job: null,
            errorMessage);
    }
}
