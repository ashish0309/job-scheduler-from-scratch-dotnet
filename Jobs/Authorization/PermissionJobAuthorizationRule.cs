namespace JobSchedulerPrototype.Jobs;

public sealed class PermissionJobAuthorizationRule : IJobAuthorizationRule
{
    private readonly string _permission;

    public PermissionJobAuthorizationRule(string permission, string _)
    {
        _permission = permission;
    }

    public JobAuthorizationRuleKind Kind => JobAuthorizationRuleKind.Grant;

    public ValueTask<JobAuthorizationResult> EvaluateAsync(
        JobActor actor,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(
            actor.HasPermission(_permission)
                ? JobAuthorizationResult.Allow()
                : JobAuthorizationResult.Skip());
    }
}
