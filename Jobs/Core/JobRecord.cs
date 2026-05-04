using System.Text.Json;

namespace JobSchedulerPrototype.Jobs;

public sealed record JobRecord
{
    public Guid Id { get; private init; }
    public string Type { get; private init; }
    public JsonElement Payload { get; private init; }
    public JobStatus Status { get; private init; }
    public Guid CurrentStateChangeId { get; private init; }
    public int MaxAttempts { get; private init; }
    public string? FailureReason { get; private init; }
    public IReadOnlyList<JobStateChange> History { get; private init; }

    public DateTimeOffset EnqueuedAt => History[0].ChangedAt;

    public DateTimeOffset? StartedAt => ChangedAt(JobStatus.Running);

    public DateTimeOffset? CompletedAt => ChangedAt(JobStatus.Completed);

    public DateTimeOffset? FailedAt => ChangedAt(JobStatus.Failed);

    public int AttemptCount => History.Count(change => change.Status == JobStatus.Running);

    public bool RetryAvailable => Status == JobStatus.Failed && AttemptCount < MaxAttempts;

    private JobRecord(
        Guid id,
        string type,
        JsonElement payload,
        JobStatus status,
        Guid currentStateChangeId,
        int maxAttempts,
        string? failureReason,
        IReadOnlyList<JobStateChange> history)
    {
        Id = id;
        Type = type;
        Payload = payload;
        Status = status;
        CurrentStateChangeId = currentStateChangeId;
        MaxAttempts = maxAttempts;
        FailureReason = failureReason;
        History = history;
    }

    public static JobRecord Enqueue(
        Guid id,
        string type,
        JsonElement payload,
        int maxAttempts,
        DateTimeOffset enqueuedAt)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Max attempts must be at least 1.");
        }

        var queuedChange = JobStateChange.Create(
            JobStatus.Queued,
            enqueuedAt,
            "Job accepted.");

        return new JobRecord(
            id,
            type,
            payload,
            JobStatus.Queued,
            queuedChange.Id,
            maxAttempts,
            failureReason: null,
            [queuedChange]);
    }

    public JobRecord TransitionTo(JobStatus nextStatus, DateTimeOffset changedAt)
    {
        var stateChange = JobStateChange.Create(nextStatus, changedAt, ReasonFor(nextStatus));

        return this with
        {
            Status = nextStatus,
            CurrentStateChangeId = stateChange.Id,
            History = [.. History, stateChange]
        };
    }

    public JobRecord Retry(DateTimeOffset queuedAt)
    {
        var stateChange = JobStateChange.Create(
            JobStatus.Queued,
            queuedAt,
            "Manually retried.");

        return this with
        {
            Status = JobStatus.Queued,
            CurrentStateChangeId = stateChange.Id,
            FailureReason = null,
            History = [.. History, stateChange]
        };
    }

    public JobRecord TransitionToFailed(string reason, DateTimeOffset changedAt)
    {
        var stateChange = JobStateChange.Create(JobStatus.Failed, changedAt, reason);

        return this with
        {
            Status = JobStatus.Failed,
            CurrentStateChangeId = stateChange.Id,
            FailureReason = reason,
            History = [.. History, stateChange]
        };
    }

    public DateTimeOffset QueuedAt()
    {
        return EnqueuedAt;
    }

    private DateTimeOffset? ChangedAt(JobStatus status)
    {
        return History.LastOrDefault(change => change.Status == status)?.ChangedAt;
    }

    private static string ReasonFor(JobStatus status)
    {
        return status switch
        {
            JobStatus.Queued => "Job queued.",
            JobStatus.Running => "Worker claimed job.",
            JobStatus.Completed => "Job completed successfully.",
            JobStatus.Failed => "Job failed.",
            _ => "Job state changed."
        };
    }
}

public sealed record JobStateChange(
    Guid Id,
    JobStatus Status,
    DateTimeOffset ChangedAt,
    string Reason)
{
    public static JobStateChange Create(
        JobStatus status,
        DateTimeOffset changedAt,
        string reason)
    {
        return new JobStateChange(
            Guid.NewGuid(),
            status,
            changedAt,
            reason);
    }
}
