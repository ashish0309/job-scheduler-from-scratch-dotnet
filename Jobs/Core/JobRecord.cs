using System.Text.Json;

namespace JobSchedulerPrototype.Jobs;

public sealed record JobRecord : ITenantScoped
{
    private readonly List<JobStateChange> _history = [];

    public Guid Id { get; private init; }
    public string TenantId { get; private init; }
    public string CreatedByActorId { get; private init; }
    public string Type { get; private init; }
    public JsonElement Payload { get; private init; }
    public JobStatus Status { get; private init; }
    public Guid CurrentStateChangeId { get; private init; }
    public DateTimeOffset? RunAt { get; private init; }
    public string? ClaimedBy { get; private init; }
    public DateTimeOffset? ClaimedAt { get; private init; }
    public DateTimeOffset? LeaseExpiresAt { get; private init; }
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
        TenantId = string.Empty;
        CreatedByActorId = string.Empty;
        Type = string.Empty;
    }

    private JobRecord(
        Guid id,
        string tenantId,
        string createdByActorId,
        string type,
        JsonElement payload,
        JobStatus status,
        Guid currentStateChangeId,
        DateTimeOffset? runAt,
        string? claimedBy,
        DateTimeOffset? claimedAt,
        DateTimeOffset? leaseExpiresAt,
        int maxAttempts,
        string? failureReason,
        IReadOnlyList<JobStateChange> history)
    {
        Id = id;
        TenantId = tenantId;
        CreatedByActorId = createdByActorId;
        Type = type;
        Payload = payload;
        Status = status;
        CurrentStateChangeId = currentStateChangeId;
        RunAt = runAt;
        ClaimedBy = claimedBy;
        ClaimedAt = claimedAt;
        LeaseExpiresAt = leaseExpiresAt;
        MaxAttempts = maxAttempts;
        FailureReason = failureReason;
        _history = history
            .Select((change, index) => change.WithSequence(index + 1))
            .ToList();
    }

    public static JobRecord Enqueue(
        Guid id,
        string tenantId,
        string createdByActorId,
        string type,
        JsonElement payload,
        int maxAttempts,
        DateTimeOffset enqueuedAt)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Max attempts must be at least 1.");
        }

        ValidateOwnership(tenantId, createdByActorId);

        var queuedChange = JobStateChange.Queued(
            enqueuedAt,
            "Job accepted.");

        return new JobRecord(
            id,
            tenantId,
            createdByActorId,
            type,
            payload,
            JobStatus.Queued,
            queuedChange.Id,
            enqueuedAt,
            claimedBy: null,
            claimedAt: null,
            leaseExpiresAt: null,
            maxAttempts,
            failureReason: null,
            [queuedChange]);
    }

    public static JobRecord Schedule(
        Guid id,
        string tenantId,
        string createdByActorId,
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

        ValidateOwnership(tenantId, createdByActorId);

        var scheduledChange = JobStateChange.Scheduled(
            changedAt,
            "Job scheduled.",
            scheduledAt);

        return new JobRecord(
            id,
            tenantId,
            createdByActorId,
            type,
            payload,
            JobStatus.Scheduled,
            scheduledChange.Id,
            scheduledAt,
            claimedBy: null,
            claimedAt: null,
            leaseExpiresAt: null,
            maxAttempts,
            failureReason: null,
            [scheduledChange]);
    }

    public JobRecord TransitionTo(JobStatus nextStatus, DateTimeOffset changedAt)
    {
        var stateChange = StateChangeFor(nextStatus, changedAt, ReasonFor(nextStatus));

        return new JobRecord(
            Id,
            TenantId,
            CreatedByActorId,
            Type,
            Payload,
            nextStatus,
            stateChange.Id,
            RunAtFor(nextStatus, changedAt),
            claimedBy: null,
            claimedAt: null,
            leaseExpiresAt: null,
            MaxAttempts,
            FailureReason,
            [.. History, stateChange]);
    }

    public JobRecord Claim(
        string workerId,
        DateTimeOffset claimedAt,
        DateTimeOffset leaseExpiresAt)
    {
        if (string.IsNullOrWhiteSpace(workerId))
        {
            throw new ArgumentException("Worker ID is required.", nameof(workerId));
        }

        if (leaseExpiresAt <= claimedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseExpiresAt),
                "Lease expiry must be after the claim time.");
        }

        var stateChange = JobStateChange.Running(
            claimedAt,
            $"Worker {workerId} claimed job.");

        return new JobRecord(
            Id,
            TenantId,
            CreatedByActorId,
            Type,
            Payload,
            JobStatus.Running,
            stateChange.Id,
            runAt: null,
            claimedBy: workerId,
            claimedAt,
            leaseExpiresAt,
            MaxAttempts,
            FailureReason,
            [.. History, stateChange]);
    }

    public JobRecord ReclaimExpiredLease(
        string workerId,
        DateTimeOffset claimedAt,
        DateTimeOffset leaseExpiresAt)
    {
        if (Status != JobStatus.Running)
        {
            throw new InvalidOperationException("Only running jobs can have an expired lease reclaimed.");
        }

        if (LeaseExpiresAt is null || LeaseExpiresAt > claimedAt)
        {
            throw new InvalidOperationException("Only expired job leases can be reclaimed.");
        }

        if (string.IsNullOrWhiteSpace(workerId))
        {
            throw new ArgumentException("Worker ID is required.", nameof(workerId));
        }

        if (leaseExpiresAt <= claimedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseExpiresAt),
                "Lease expiry must be after the claim time.");
        }

        var stateChange = JobStateChange.Running(
            claimedAt,
            $"Worker {workerId} reclaimed expired lease.");

        return new JobRecord(
            Id,
            TenantId,
            CreatedByActorId,
            Type,
            Payload,
            JobStatus.Running,
            stateChange.Id,
            runAt: null,
            claimedBy: workerId,
            claimedAt,
            leaseExpiresAt,
            MaxAttempts,
            FailureReason,
            [.. History, stateChange]);
    }

    public JobRecord RenewLease(DateTimeOffset renewedAt, DateTimeOffset leaseExpiresAt)
    {
        if (Status != JobStatus.Running)
        {
            throw new InvalidOperationException("Only running jobs can renew a lease.");
        }

        if (leaseExpiresAt <= renewedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseExpiresAt),
                "Lease expiry must be after the renewal time.");
        }

        return new JobRecord(
            Id,
            TenantId,
            CreatedByActorId,
            Type,
            Payload,
            Status,
            CurrentStateChangeId,
            RunAt,
            ClaimedBy,
            ClaimedAt,
            leaseExpiresAt,
            MaxAttempts,
            FailureReason,
            History);
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
            TenantId,
            CreatedByActorId,
            Type,
            Payload,
            JobStatus.Scheduled,
            scheduledChange.Id,
            scheduledAt,
            claimedBy: null,
            claimedAt: null,
            leaseExpiresAt: null,
            MaxAttempts,
            failureReason,
            [.. History, failedChange, scheduledChange]);
    }

    public JobRecord TransitionToFailed(string reason, DateTimeOffset changedAt)
    {
        var stateChange = JobStateChange.Failed(changedAt, reason);

        return new JobRecord(
            Id,
            TenantId,
            CreatedByActorId,
            Type,
            Payload,
            JobStatus.Failed,
            stateChange.Id,
            runAt: null,
            claimedBy: null,
            claimedAt: null,
            leaseExpiresAt: null,
            MaxAttempts,
            reason,
            [.. History, stateChange]);
    }

    public DateTimeOffset QueuedAt()
    {
        return EnqueuedAt;
    }

    internal JobRecord WithOrderedHistory()
    {
        return new JobRecord(
            Id,
            TenantId,
            CreatedByActorId,
            Type,
            Payload,
            Status,
            CurrentStateChangeId,
            RunAt,
            ClaimedBy,
            ClaimedAt,
            LeaseExpiresAt,
            MaxAttempts,
            FailureReason,
            History
                .OrderBy(change => change.Sequence)
                .ThenBy(change => change.ChangedAt)
                .ThenBy(change => change.Id)
                .ToArray());
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

    private static void ValidateOwnership(string tenantId, string createdByActorId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(createdByActorId))
        {
            throw new ArgumentException("Created-by actor ID is required.", nameof(createdByActorId));
        }
    }

    private static DateTimeOffset? RunAtFor(JobStatus status, DateTimeOffset changedAt)
    {
        return status == JobStatus.Queued
            ? changedAt
            : null;
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
