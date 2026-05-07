namespace JobSchedulerPrototype.Jobs;

public sealed class PermissionJobAuthorizationRule : IJobAuthorizationRule
{
    private readonly string _permission;
    private readonly string _errorMessage;

    public PermissionJobAuthorizationRule(string permission, string errorMessage)
    {
        _permission = permission;
        _errorMessage = errorMessage;
    }

    public ValueTask<JobAuthorizationResult> EvaluateAsync(
        JobActor actor,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(
            actor.HasPermission(_permission)
                ? JobAuthorizationResult.Allow()
                : JobAuthorizationResult.Deny(_errorMessage));
    }
}
