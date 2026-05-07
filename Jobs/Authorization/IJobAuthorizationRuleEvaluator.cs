namespace JobSchedulerPrototype.Jobs;

public interface IJobAuthorizationRuleEvaluator
{
    ValueTask<JobAuthorizationResult> EvaluateAsync(
        JobActor actor,
        IReadOnlyList<IJobAuthorizationRule> rules,
        CancellationToken cancellationToken);
}
