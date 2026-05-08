namespace JobSchedulerPrototype.Jobs;

public sealed class AcknowledgeJobAction : JobAuthorizedAction<AcknowledgeJobActionRequest, AcknowledgeJobActionResult>
{
    private readonly IJobStore _jobs;

    public AcknowledgeJobAction(
        IJobStore jobs,
        IJobActorProvider actorProvider,
        IJobAuthorizationRuleEvaluator ruleEvaluator,
        IDataAccessScopeProvider dataAccessScopeProvider)
        : base(actorProvider, ruleEvaluator, dataAccessScopeProvider)
    {
        _jobs = jobs;
    }

    protected override DataAccessOperation DataAccessOperation => DataAccessOperation.Mutate;

    protected override DataAccessScope BuildDataAccessScope(
        AcknowledgeJobActionRequest request,
        JobActor actor)
    {
        return actor.HasPermission(JobPermissions.GlobalRead)
            ? DataAccessScope.AllTenants()
            : DataAccessScope.Tenant(actor.TenantId);
    }

    protected override IReadOnlyList<IJobAuthorizationRule> BuildAuthorizationRules(AcknowledgeJobActionRequest request)
    {
        const string errorMessage = "Actor is not authorized to acknowledge jobs.";

        return
        [
            new PermissionJobAuthorizationRule(
                JobPermissions.EmailManage,
                errorMessage)
        ];
    }

    protected override AcknowledgeJobActionResult OnAuthorizationDenied(JobAuthorizationResult result)
    {
        return AcknowledgeJobActionResult.Denied(
            result.ErrorMessage ?? "Actor is not authorized to acknowledge jobs.");
    }

    protected override Task<AcknowledgeJobActionResult> ExecuteAuthorizedAsync(
        AcknowledgeJobActionRequest request,
        JobActor actor,
        CancellationToken cancellationToken)
    {
        var acknowledged = _jobs.Acknowledge(request.Id, actor.Id, DateTimeOffset.UtcNow);

        return Task.FromResult(AcknowledgeJobActionResult.Authorized(acknowledged));
    }
}

public sealed record AcknowledgeJobActionRequest(
    Guid Id) : IJobActionRequest<AcknowledgeJobActionResult>;

public sealed record AcknowledgeJobActionResult(
    bool IsAuthorized,
    bool Acknowledged,
    string? ErrorMessage)
{
    public static AcknowledgeJobActionResult Authorized(bool acknowledged)
    {
        return new AcknowledgeJobActionResult(
            true,
            acknowledged,
            ErrorMessage: null);
    }

    public static AcknowledgeJobActionResult Denied(string errorMessage)
    {
        return new AcknowledgeJobActionResult(
            false,
            Acknowledged: false,
            errorMessage);
    }
}
