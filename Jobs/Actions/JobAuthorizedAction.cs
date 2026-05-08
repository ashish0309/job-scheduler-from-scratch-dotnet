namespace JobSchedulerPrototype.Jobs;

public abstract class JobAuthorizedAction<TRequest, TResponse> : IJobActionHandler<TRequest, TResponse>
    where TRequest : IJobActionRequest<TResponse>
{
    private readonly IJobActorProvider _actorProvider;
    private readonly IJobAuthorizationRuleEvaluator _ruleEvaluator;
    private readonly IDataAccessScopeProvider _dataAccessScopeProvider;

    protected JobAuthorizedAction(
        IJobActorProvider actorProvider,
        IJobAuthorizationRuleEvaluator ruleEvaluator,
        IDataAccessScopeProvider dataAccessScopeProvider)
    {
        _actorProvider = actorProvider;
        _ruleEvaluator = ruleEvaluator;
        _dataAccessScopeProvider = dataAccessScopeProvider;
    }

    public async Task<TResponse> ExecuteAsync(
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        var rules = BuildAuthorizationRules(request);
        if (rules.Count == 0)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} must define at least one authorization rule.");
        }

        var actor = _dataAccessScopeProvider.ScopedActor
            ?? _actorProvider.GetCurrentActor();
        var authorization = await _ruleEvaluator.EvaluateAsync(
            actor,
            rules,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            return OnAuthorizationDenied(authorization);
        }

        using var scope = _dataAccessScopeProvider.BeginScope(
            BuildDataAccessScope(request, actor),
            DataAccessOperation);

        return await ExecuteAuthorizedAsync(request, actor, cancellationToken);
    }

    protected virtual DataAccessOperation DataAccessOperation =>
        global::JobSchedulerPrototype.Jobs.DataAccessOperation.Read;

    protected virtual DataAccessScope BuildDataAccessScope(
        TRequest request,
        JobActor actor)
    {
        return DataAccessScope.Tenant(actor.TenantId);
    }

    protected abstract IReadOnlyList<IJobAuthorizationRule> BuildAuthorizationRules(TRequest request);

    protected abstract TResponse OnAuthorizationDenied(JobAuthorizationResult result);

    protected abstract Task<TResponse> ExecuteAuthorizedAsync(
        TRequest request,
        JobActor actor,
        CancellationToken cancellationToken);
}
