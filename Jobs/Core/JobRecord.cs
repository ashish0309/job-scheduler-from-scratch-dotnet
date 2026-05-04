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

    public DateTimeOffset? ScheduledAt => History
        .LastOrDefault(change => change.Status == JobStatus.Scheduled)
        ?.ScheduledAt;

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

        var queuedChange = JobStateChange.Queued(
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

    public static JobRecord Schedule(
        Guid id,
        string type,
        JsonElement payload,
        int maxAttempts,
        DateTimeOffset scheduledAt,
        DateTimeOffset changedAt)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Max attempts must be at least 1.");
        }

        var scheduledChange = JobStateChange.Scheduled(
            changedAt,
            "Job scheduled.",
            scheduledAt);

        return new JobRecord(
            id,
            type,
            payload,
            JobStatus.Scheduled,
            scheduledChange.Id,
            maxAttempts,
            failureReason: null,
            [scheduledChange]);
    }

    public JobRecord TransitionTo(JobStatus nextStatus, DateTimeOffset changedAt)
    {
        var stateChange = StateChangeFor(nextStatus, changedAt, ReasonFor(nextStatus));

        return this with
        {
            Status = nextStatus,
            CurrentStateChangeId = stateChange.Id,
            History = [.. History, stateChange]
        };
    }

    public JobRecord Retry(DateTimeOffset queuedAt)
    {
        var stateChange = JobStateChange.Queued(
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
        var stateChange = JobStateChange.Failed(changedAt, reason);

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
            JobStatus.Scheduled => "Job scheduled.",
            JobStatus.Queued => "Job queued.",
            JobStatus.Running => "Worker claimed job.",
            JobStatus.Completed => "Job completed successfully.",
            JobStatus.Failed => "Job failed.",
            _ => "Job state changed."
        };
    }

    private static JobStateChange StateChangeFor(
        JobStatus status,
        DateTimeOffset changedAt,
        string reason)
    {
        return status switch
        {
            JobStatus.Queued => JobStateChange.Queued(changedAt, reason),
            JobStatus.Running => JobStateChange.Running(changedAt, reason),
            JobStatus.Completed => JobStateChange.Completed(changedAt, reason),
            JobStatus.Failed => JobStateChange.Failed(changedAt, reason),
            JobStatus.Scheduled => throw new InvalidOperationException(
                "Use Schedule to create scheduled state changes."),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported job status.")
        };
    }
}

public sealed record JobStateChange
{
    private JobStateChange(
        Guid id,
        JobStatus status,
        DateTimeOffset changedAt,
        string reason,
        JobStateDetails details)
    {
        Id = id;
        Status = status;
        ChangedAt = changedAt;
        Reason = reason;
        Details = details;
    }

    public Guid Id { get; }

    public JobStatus Status { get; }

    public DateTimeOffset ChangedAt { get; }

    public string Reason { get; }

    public JobStateDetails Details { get; }

    public DateTimeOffset? ScheduledAt => Details is ScheduledJobStateDetails details
        ? details.ScheduledAt
        : null;

    public static JobStateChange Queued(DateTimeOffset changedAt, string reason)
    {
        return Create(JobStatus.Queued, changedAt, reason, JobStateDetails.None);
    }

    public static JobStateChange Scheduled(
        DateTimeOffset changedAt,
        string reason,
        DateTimeOffset scheduledAt)
    {
        return Create(
            JobStatus.Scheduled,
            changedAt,
            reason,
            new ScheduledJobStateDetails(scheduledAt));
    }

    public static JobStateChange Running(DateTimeOffset changedAt, string reason)
    {
        return Create(JobStatus.Running, changedAt, reason, JobStateDetails.None);
    }

    public static JobStateChange Completed(DateTimeOffset changedAt, string reason)
    {
        return Create(JobStatus.Completed, changedAt, reason, JobStateDetails.None);
    }

    public static JobStateChange Failed(DateTimeOffset changedAt, string reason)
    {
        return Create(JobStatus.Failed, changedAt, reason, JobStateDetails.None);
    }

    private static JobStateChange Create(
        JobStatus status,
        DateTimeOffset changedAt,
        string reason,
        JobStateDetails details)
    {
        return new JobStateChange(
            Guid.NewGuid(),
            status,
            changedAt,
            reason,
            details);
    }
}

public abstract record JobStateDetails
{
    public static JobStateDetails None { get; } = new EmptyJobStateDetails();

    private sealed record EmptyJobStateDetails : JobStateDetails;
}

public sealed record ScheduledJobStateDetails(DateTimeOffset ScheduledAt) : JobStateDetails;
