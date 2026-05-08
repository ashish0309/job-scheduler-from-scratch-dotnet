namespace JobSchedulerPrototype.Jobs;

public sealed class AnyOfJobAuthorizationRule : IJobAuthorizationRule
{
    private readonly IReadOnlyList<IJobAuthorizationRule> _rules;

    public AnyOfJobAuthorizationRule(
        IReadOnlyList<IJobAuthorizationRule> rules,
        string _)
    {
        if (rules.Count == 0)
        {
            throw new ArgumentException(
                "At least one authorization rule is required.",
                nameof(rules));
        }

        _rules = rules;
    }

    public JobAuthorizationRuleKind Kind => JobAuthorizationRuleKind.Grant;

    public async ValueTask<JobAuthorizationResult> EvaluateAsync(
        JobActor actor,
        CancellationToken cancellationToken)
    {
        var anyAllow = false;

        foreach (var rule in _rules)
        {
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
