namespace JobSchedulerPrototype.Jobs;

public sealed record JobExecutionResult(
    bool Succeeded,
    string? FailureReason)
{
    public static JobExecutionResult Success()
    {
        return new JobExecutionResult(true, FailureReason: null);
    }

    public static JobExecutionResult Failure(string reason)
    {
        return new JobExecutionResult(false, reason);
    }
}
