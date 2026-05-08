namespace JobSchedulerPrototype.Jobs;

public sealed class OwnerActorJobAuthorizationRule : IJobAuthorizationRule
{
    private readonly string? _ownerActorId;

    public OwnerActorJobAuthorizationRule(
        string? ownerActorId,
        string _)
    {
        _ownerActorId = ownerActorId;
    }

    public JobAuthorizationRuleKind Kind => JobAuthorizationRuleKind.Grant;

    public ValueTask<JobAuthorizationResult> EvaluateAsync(
        JobActor actor,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_ownerActorId)
            && string.Equals(_ownerActorId, actor.Id, StringComparison.Ordinal))
        {
            return ValueTask.FromResult(JobAuthorizationResult.Allow());
        }

        return ValueTask.FromResult(JobAuthorizationResult.Skip());
    }
}
