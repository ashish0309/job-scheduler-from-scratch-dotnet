using System.Text.Json;

namespace JobSchedulerPrototype.Jobs;

public interface IJobLifecycleService
{
    JobEnqueueResult Enqueue(string type, JsonElement payload, int? delaySeconds);

    JobRecord? ClaimNextDueJob(
        DateTimeOffset now,
        string workerId,
        DateTimeOffset leaseExpiresAt);

    JobExecutionCompletion CompleteExecution(JobRecord job, JobExecutionResult result);
}

public sealed record JobEnqueueResult(
    bool Accepted,
    JobRecord? Job,
    string? ErrorMessage)
{
    public static JobEnqueueResult Success(JobRecord job)
    {
        return new JobEnqueueResult(true, job, ErrorMessage: null);
    }

    public static JobEnqueueResult Rejected(string errorMessage)
    {
        return new JobEnqueueResult(false, Job: null, errorMessage);
    }
}

public sealed record JobExecutionCompletion(
    JobExecutionCompletionStatus Status,
    string? FailureReason,
    DateTimeOffset? RetryScheduledAt)
{
    public static JobExecutionCompletion Completed()
    {
        return new JobExecutionCompletion(
            JobExecutionCompletionStatus.Completed,
            FailureReason: null,
            RetryScheduledAt: null);
    }

    public static JobExecutionCompletion RetryScheduled(
        string failureReason,
        DateTimeOffset scheduledAt)
    {
        return new JobExecutionCompletion(
            JobExecutionCompletionStatus.RetryScheduled,
            failureReason,
            scheduledAt);
    }

    public static JobExecutionCompletion Failed(string failureReason)
    {
        return new JobExecutionCompletion(
            JobExecutionCompletionStatus.Failed,
            failureReason,
            RetryScheduledAt: null);
    }

    public static JobExecutionCompletion LeaseLost()
    {
        return new JobExecutionCompletion(
            JobExecutionCompletionStatus.LeaseLost,
            FailureReason: null,
            RetryScheduledAt: null);
    }
}

public enum JobExecutionCompletionStatus
{
    Completed,
    RetryScheduled,
    Failed,
    LeaseLost
}
