namespace JobSchedulerPrototype.Jobs;

public sealed class JobAuthorizationRuleEvaluator : IJobAuthorizationRuleEvaluator
{
    public async ValueTask<JobAuthorizationResult> EvaluateAsync(
        JobActor actor,
        IReadOnlyList<IJobAuthorizationRule> rules,
        CancellationToken cancellationToken)
    {
        var boundaryDecision = await EvaluateRulesByKindAsync(
            actor,
            rules,
            JobAuthorizationRuleKind.Deny,
            cancellationToken);
        if (boundaryDecision.Decision == JobAuthorizationDecision.Deny)
        {
            return boundaryDecision;
        }

        var grantDecision = await EvaluateRulesByKindAsync(
            actor,
            rules,
            JobAuthorizationRuleKind.Grant,
            cancellationToken);
        if (grantDecision.Decision == JobAuthorizationDecision.Deny)
        {
            return grantDecision;
        }

        return grantDecision.Decision == JobAuthorizationDecision.Allow
            ? JobAuthorizationResult.Allow()
            : JobAuthorizationResult.Deny();
    }

    private static async ValueTask<JobAuthorizationResult> EvaluateRulesByKindAsync(
        JobActor actor,
        IReadOnlyList<IJobAuthorizationRule> rules,
        JobAuthorizationRuleKind kind,
        CancellationToken cancellationToken)
    {
        var anyAllow = false;

        foreach (var rule in rules)
        {
            if (rule.Kind != kind)
            {
                continue;
            }

            var result = await rule.EvaluateAsync(actor, cancellationToken);

            if (result.Decision == JobAuthorizationDecision.Deny)
            {
                return result;
            }

            if (result.Decision == JobAuthorizationDecision.Allow)
            {
                anyAllow = true;
            }
        }

        return anyAllow
            ? JobAuthorizationResult.Allow()
            : JobAuthorizationResult.Skip();
    }
}
