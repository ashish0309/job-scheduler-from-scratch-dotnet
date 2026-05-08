namespace JobSchedulerPrototype.Jobs;

public interface IJobAuthorizationRule
{
    JobAuthorizationRuleKind Kind { get; }

    ValueTask<JobAuthorizationResult> EvaluateAsync(
        JobActor actor,
        CancellationToken cancellationToken);
}
