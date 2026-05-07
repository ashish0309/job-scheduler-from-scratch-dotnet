namespace JobSchedulerPrototype.Jobs;

public sealed record JobAuthorizationResult(
    bool IsAuthorized,
    string? ErrorMessage)
{
    public static JobAuthorizationResult Allow()
    {
        return new JobAuthorizationResult(true, ErrorMessage: null);
    }

    public static JobAuthorizationResult Deny(string errorMessage)
    {
        return new JobAuthorizationResult(false, errorMessage);
    }
}
