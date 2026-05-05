namespace JobSchedulerPrototype.Jobs;

public sealed record JobStateChange
{
    private JobStateChange()
    {
        Reason = string.Empty;
    }

    private JobStateChange(
        Guid id,
        JobStatus status,
        DateTimeOffset changedAt,
        string reason,
        DateTimeOffset? scheduledAt,
        int sequence)
    {
        Id = id;
        Status = status;
        ChangedAt = changedAt;
        Reason = reason;
        ScheduledAt = scheduledAt;
        Sequence = sequence;
    }

    public Guid Id { get; }

    public JobStatus Status { get; }

    public DateTimeOffset ChangedAt { get; }

    public string Reason { get; }

    public int Sequence { get; private init; }

    public JobStateDetails Details => ScheduledAt is { } scheduledAt
        ? new ScheduledJobStateDetails(scheduledAt)
        : JobStateDetails.None;

    public DateTimeOffset? ScheduledAt { get; private init; }

    public static JobStateChange Queued(DateTimeOffset changedAt, string reason)
    {
        return Create(JobStatus.Queued, changedAt, reason, scheduledAt: null);
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
            scheduledAt);
    }

    public static JobStateChange Running(DateTimeOffset changedAt, string reason)
    {
        return Create(JobStatus.Running, changedAt, reason, scheduledAt: null);
    }

    public static JobStateChange Completed(DateTimeOffset changedAt, string reason)
    {
        return Create(JobStatus.Completed, changedAt, reason, scheduledAt: null);
    }

    public static JobStateChange Failed(DateTimeOffset changedAt, string reason)
    {
        return Create(JobStatus.Failed, changedAt, reason, scheduledAt: null);
    }

    private static JobStateChange Create(
        JobStatus status,
        DateTimeOffset changedAt,
        string reason,
        DateTimeOffset? scheduledAt)
    {
        return new JobStateChange(
            Guid.NewGuid(),
            status,
            changedAt,
            reason,
            scheduledAt,
            sequence: 0);
    }

    internal JobStateChange WithSequence(int sequence)
    {
        return this with { Sequence = sequence };
    }
}
