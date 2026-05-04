namespace JobSchedulerPrototype.Jobs;

public interface IJobStore
{
    void Add(JobRecord job);

    JobRecord? Get(Guid id);

    IReadOnlyCollection<JobRecord> List();

    JobRecord? TryClaimNextQueuedJob();

    bool MarkCompleted(Guid id);

    bool MarkFailed(Guid id, string reason);

    bool Retry(Guid id);
}
