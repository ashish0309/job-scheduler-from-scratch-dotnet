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

    bool MarkCompleted(Guid id);

    bool MarkFailed(Guid id, string reason);

    bool ScheduleRetry(Guid id, string reason, DateTimeOffset scheduledAt);
}
