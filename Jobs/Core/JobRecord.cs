using System.Text.Json;

namespace JobSchedulerPrototype.Jobs;

public sealed record JobRecord
{
    public Guid Id { get; private init; }
    public string Type { get; private init; }
    public JsonElement Payload { get; private init; }
    public JobStatus Status { get; private init; }
    public string? FailureReason { get; private init; }
    public IReadOnlyList<JobStateChange> History { get; private init; }

    public DateTimeOffset EnqueuedAt => History[0].ChangedAt;

    public DateTimeOffset? StartedAt => ChangedAt(JobStatus.Running);

    public DateTimeOffset? CompletedAt => ChangedAt(JobStatus.Completed);

    public DateTimeOffset? FailedAt => ChangedAt(JobStatus.Failed);

    private JobRecord(
        Guid id,
        string type,
        JsonElement payload,
        JobStatus status,
        string? failureReason,
        IReadOnlyList<JobStateChange> history)
    {
        Id = id;
        Type = type;
        Payload = payload;
        Status = status;
        FailureReason = failureReason;
        History = history;
    }

    public static JobRecord Enqueue(
        Guid id,
        string type,
        JsonElement payload,
        DateTimeOffset enqueuedAt)
    {
        return new JobRecord(
            id,
            type,
            payload,
            JobStatus.Queued,
            failureReason: null,
            [new JobStateChange(JobStatus.Queued, enqueuedAt)]);
    }

    public JobRecord TransitionTo(JobStatus nextStatus, DateTimeOffset changedAt)
    {
        return this with
        {
            Status = nextStatus,
            History = [.. History, new JobStateChange(nextStatus, changedAt)]
        };
    }

    public JobRecord TransitionToFailed(string reason, DateTimeOffset changedAt)
    {
        return this with
        {
            Status = JobStatus.Failed,
            FailureReason = reason,
            History = [.. History, new JobStateChange(JobStatus.Failed, changedAt)]
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
}

public sealed record JobStateChange(
    JobStatus Status,
    DateTimeOffset ChangedAt);
