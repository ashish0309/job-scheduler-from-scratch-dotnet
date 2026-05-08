namespace JobSchedulerPrototype.Jobs;

public sealed record JobAuthorizationResult(
    JobAuthorizationDecision Decision,
    string? ErrorMessage)
{
    public bool IsAuthorized => Decision == JobAuthorizationDecision.Allow;

    public static JobAuthorizationResult Allow()
    {
        return new JobAuthorizationResult(
            JobAuthorizationDecision.Allow,
            ErrorMessage: null);
    }

    public static JobAuthorizationResult Skip()
    {
        return new JobAuthorizationResult(
            JobAuthorizationDecision.Skip,
            ErrorMessage: null);
    }

    public static JobAuthorizationResult Deny(string? errorMessage = null)
    {
        return new JobAuthorizationResult(
            JobAuthorizationDecision.Deny,
            errorMessage);
    }
}
