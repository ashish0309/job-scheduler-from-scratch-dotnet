namespace JobSchedulerPrototype.Jobs;

public abstract class JobAuthorizedAction<TRequest, TResponse> : IJobActionHandler<TRequest, TResponse>
    where TRequest : IJobActionRequest<TResponse>
{
    private readonly IJobActorProvider _actorProvider;
    private readonly IJobAuthorizationRuleEvaluator _ruleEvaluator;

    protected JobAuthorizedAction(
        IJobActorProvider actorProvider,
        IJobAuthorizationRuleEvaluator ruleEvaluator)
    {
        _actorProvider = actorProvider;
        _ruleEvaluator = ruleEvaluator;
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

        var actor = _actorProvider.GetCurrentActor();
        var authorization = await _ruleEvaluator.EvaluateAsync(
            actor,
            rules,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            return OnAuthorizationDenied(authorization);
        }

        return await ExecuteAuthorizedAsync(request, actor, cancellationToken);
    }

    protected abstract IReadOnlyList<IJobAuthorizationRule> BuildAuthorizationRules(TRequest request);

    protected abstract TResponse OnAuthorizationDenied(JobAuthorizationResult result);

    protected abstract Task<TResponse> ExecuteAuthorizedAsync(
        TRequest request,
        JobActor actor,
        CancellationToken cancellationToken);
}
