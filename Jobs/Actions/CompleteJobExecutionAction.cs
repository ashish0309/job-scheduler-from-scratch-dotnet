namespace JobSchedulerPrototype.Jobs;

public sealed class CompleteJobExecutionAction : JobAuthorizedAction<CompleteJobExecutionActionRequest, CompleteJobExecutionActionResult>
{
    private readonly IJobLifecycleService _lifecycle;

    public CompleteJobExecutionAction(
        IJobLifecycleService lifecycle,
        IJobActorProvider actorProvider,
        IJobAuthorizationRuleEvaluator ruleEvaluator)
        : base(actorProvider, ruleEvaluator)
    {
        _lifecycle = lifecycle;
    }

    protected override IReadOnlyList<IJobAuthorizationRule> BuildAuthorizationRules(CompleteJobExecutionActionRequest request)
    {
        return
        [
            new PermissionJobAuthorizationRule(
                JobPermissions.Execute,
                "Actor is not authorized to complete job executions.")
        ];
    }

    protected override CompleteJobExecutionActionResult OnAuthorizationDenied(JobAuthorizationResult result)
    {
        return CompleteJobExecutionActionResult.Denied(
            result.ErrorMessage ?? "Actor is not authorized to complete job executions.");
    }

    protected override Task<CompleteJobExecutionActionResult> ExecuteAuthorizedAsync(
        CompleteJobExecutionActionRequest request,
        JobActor actor,
        CancellationToken cancellationToken)
    {
        var completion = _lifecycle.CompleteExecution(request.Job, request.Result);

        return Task.FromResult(CompleteJobExecutionActionResult.Authorized(completion));
    }
}

public sealed record CompleteJobExecutionActionRequest(
    JobRecord Job,
    JobExecutionResult Result) : IJobActionRequest<CompleteJobExecutionActionResult>;

public sealed record CompleteJobExecutionActionResult(
    bool IsAuthorized,
    JobExecutionCompletion? Completion,
    string? ErrorMessage)
{
    public static CompleteJobExecutionActionResult Authorized(JobExecutionCompletion completion)
    {
        return new CompleteJobExecutionActionResult(
            true,
            completion,
            ErrorMessage: null);
    }

    public static CompleteJobExecutionActionResult Denied(string errorMessage)
    {
        return new CompleteJobExecutionActionResult(
            false,
            Completion: null,
            errorMessage);
    }
}
