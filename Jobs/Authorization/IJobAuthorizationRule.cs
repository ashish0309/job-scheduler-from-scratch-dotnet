namespace JobSchedulerPrototype.Jobs;

public interface IJobAuthorizationRule
{
    ValueTask<JobAuthorizationResult> EvaluateAsync(
        JobActor actor,
        CancellationToken cancellationToken);
}
