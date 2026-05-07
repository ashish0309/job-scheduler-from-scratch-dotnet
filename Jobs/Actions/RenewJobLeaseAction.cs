namespace JobSchedulerPrototype.Jobs;

public sealed class RenewJobLeaseAction : JobAuthorizedAction<RenewJobLeaseActionRequest, RenewJobLeaseActionResult>
{
    private readonly IJobLifecycleService _lifecycle;

    public RenewJobLeaseAction(
        IJobLifecycleService lifecycle,
        IJobActorProvider actorProvider,
        IJobAuthorizationRuleEvaluator ruleEvaluator)
        : base(actorProvider, ruleEvaluator)
    {
        _lifecycle = lifecycle;
    }

    protected override IReadOnlyList<IJobAuthorizationRule> BuildAuthorizationRules(RenewJobLeaseActionRequest request)
    {
        return
        [
            new PermissionJobAuthorizationRule(
                JobPermissions.Execute,
                "Actor is not authorized to renew job leases.")
        ];
    }

    protected override RenewJobLeaseActionResult OnAuthorizationDenied(JobAuthorizationResult result)
    {
        return RenewJobLeaseActionResult.Denied(
            result.ErrorMessage ?? "Actor is not authorized to renew job leases.");
    }

    protected override Task<RenewJobLeaseActionResult> ExecuteAuthorizedAsync(
        RenewJobLeaseActionRequest request,
        JobActor actor,
        CancellationToken cancellationToken)
    {
        var renewed = _lifecycle.RenewLease(
            request.Job,
            request.RenewedAt,
            request.LeaseExpiresAt);

        return Task.FromResult(RenewJobLeaseActionResult.Authorized(renewed));
    }
}

public sealed record RenewJobLeaseActionRequest(
    JobRecord Job,
    DateTimeOffset RenewedAt,
    DateTimeOffset LeaseExpiresAt) : IJobActionRequest<RenewJobLeaseActionResult>;

public sealed record RenewJobLeaseActionResult(
    bool IsAuthorized,
    bool Renewed,
    string? ErrorMessage)
{
    public static RenewJobLeaseActionResult Authorized(bool renewed)
    {
        return new RenewJobLeaseActionResult(
            true,
            renewed,
            ErrorMessage: null);
    }

    public static RenewJobLeaseActionResult Denied(string errorMessage)
    {
        return new RenewJobLeaseActionResult(
            false,
            Renewed: false,
            errorMessage);
    }
}
