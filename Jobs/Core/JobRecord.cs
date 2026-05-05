using System.Text.Json;

namespace JobSchedulerPrototype.Jobs;

public sealed record JobRecord
{
    private readonly List<JobStateChange> _history = [];

    public Guid Id { get; private init; }
    public string Type { get; private init; }
    public JsonElement Payload { get; private init; }
    public JobStatus Status { get; private init; }
    public Guid CurrentStateChangeId { get; private init; }
    public int MaxAttempts { get; private init; }
    public string? FailureReason { get; private init; }
    public IReadOnlyList<JobStateChange> History => _history;

    public DateTimeOffset EnqueuedAt => History[0].ChangedAt;

    public DateTimeOffset? ScheduledAt => History
        .LastOrDefault(change => change.Status == JobStatus.Scheduled)
        ?.ScheduledAt;

    public DateTimeOffset? StartedAt => ChangedAt(JobStatus.Running);

    public DateTimeOffset? CompletedAt => ChangedAt(JobStatus.Completed);

    public DateTimeOffset? FailedAt => ChangedAt(JobStatus.Failed);

    public int AttemptCount => History.Count(change => change.Status == JobStatus.Running);

    public IReadOnlyList<JobAttempt> Attempts => BuildAttempts();

    private JobRecord()
    {
        Type = string.Empty;
    }

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
        _history = history.ToList();
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

        return new JobRecord(
            Id,
            Type,
            Payload,
            nextStatus,
            stateChange.Id,
            MaxAttempts,
            FailureReason,
            [.. History, stateChange]);
    }

    public JobRecord ScheduleRetry(
        string failureReason,
        DateTimeOffset failedAt,
        DateTimeOffset scheduledAt)
    {
        var failedChange = JobStateChange.Failed(failedAt, failureReason);
        var scheduledChange = JobStateChange.Scheduled(
            failedAt,
            "Retry scheduled.",
            scheduledAt);

        return new JobRecord(
            Id,
            Type,
            Payload,
            JobStatus.Scheduled,
            scheduledChange.Id,
            MaxAttempts,
            failureReason,
            [.. History, failedChange, scheduledChange]);
    }

    public JobRecord TransitionToFailed(string reason, DateTimeOffset changedAt)
    {
        var stateChange = JobStateChange.Failed(changedAt, reason);

        return new JobRecord(
            Id,
            Type,
            Payload,
            JobStatus.Failed,
            stateChange.Id,
            MaxAttempts,
            reason,
            [.. History, stateChange]);
    }

    public DateTimeOffset QueuedAt()
    {
        return EnqueuedAt;
    }

    private DateTimeOffset? ChangedAt(JobStatus status)
    {
        return History.LastOrDefault(change => change.Status == status)?.ChangedAt;
    }

    private IReadOnlyList<JobAttempt> BuildAttempts()
    {
        List<JobAttempt> attempts = [];
        JobStateChange? runningChange = null;

        foreach (var change in History)
        {
            if (change.Status == JobStatus.Running)
            {
                runningChange = change;
                continue;
            }

            if (runningChange is null)
            {
                continue;
            }

            if (change.Status == JobStatus.Completed)
            {
                attempts.Add(JobAttempt.Completed(
                    attempts.Count + 1,
                    runningChange.ChangedAt,
                    change.ChangedAt));
                runningChange = null;
            }
            else if (change.Status == JobStatus.Failed)
            {
                attempts.Add(JobAttempt.Failed(
                    attempts.Count + 1,
                    runningChange.ChangedAt,
                    change.ChangedAt,
                    change.Reason));
                runningChange = null;
            }
        }

        if (runningChange is not null)
        {
            attempts.Add(JobAttempt.Running(
                attempts.Count + 1,
                runningChange.ChangedAt));
        }

        return attempts;
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
