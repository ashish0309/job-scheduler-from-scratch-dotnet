namespace JobSchedulerPrototype.Jobs;

public interface IJobStore
{
    void Add(JobRecord job);

    JobRecord? Get(Guid id);

    IReadOnlyCollection<JobRecord> List();

    JobRecord? TryClaimNextDueJob(
        DateTimeOffset now,
        string workerId,
        DateTimeOffset leaseExpiresAt);

    bool MarkCompleted(Guid id, Guid expectedCurrentStateChangeId);

    bool MarkFailed(Guid id, Guid expectedCurrentStateChangeId, string reason);

    bool ScheduleRetry(
        Guid id,
        Guid expectedCurrentStateChangeId,
        string reason,
        DateTimeOffset scheduledAt);
}
