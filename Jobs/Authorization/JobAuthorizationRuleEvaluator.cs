namespace JobSchedulerPrototype.Jobs;

public sealed class JobAuthorizationRuleEvaluator : IJobAuthorizationRuleEvaluator
{
    public async ValueTask<JobAuthorizationResult> EvaluateAsync(
        JobActor actor,
        IReadOnlyList<IJobAuthorizationRule> rules,
        CancellationToken cancellationToken)
    {
        foreach (var rule in rules)
        {
            var result = await rule.EvaluateAsync(actor, cancellationToken);
            if (!result.IsAuthorized)
            {
                return result;
            }
        }

        return JobAuthorizationResult.Allow();
    }
}
