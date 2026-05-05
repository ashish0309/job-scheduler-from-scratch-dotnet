namespace JobSchedulerPrototype.Jobs;

public sealed record JobAttempt(
    int Number,
    JobStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FailedAt,
    string? FailureReason)
{
    public DateTimeOffset? FinishedAt => CompletedAt ?? FailedAt;

    public TimeSpan? Duration => FinishedAt - StartedAt;

    public static JobAttempt Running(int number, DateTimeOffset startedAt)
    {
        return new JobAttempt(
            number,
            JobStatus.Running,
            startedAt,
            CompletedAt: null,
            FailedAt: null,
            FailureReason: null);
    }

    public static JobAttempt Completed(
        int number,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        return new JobAttempt(
            number,
            JobStatus.Completed,
            startedAt,
            completedAt,
            FailedAt: null,
            FailureReason: null);
    }

    public static JobAttempt Failed(
        int number,
        DateTimeOffset startedAt,
        DateTimeOffset failedAt,
        string failureReason)
    {
        return new JobAttempt(
            number,
            JobStatus.Failed,
            startedAt,
            CompletedAt: null,
            failedAt,
            failureReason);
    }
}
