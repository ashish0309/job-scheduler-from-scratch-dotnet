using System.Collections.Concurrent;

namespace JobSchedulerPrototype.Jobs;

public sealed class InMemoryJobStore : IJobStore
{
    private readonly ConcurrentDictionary<Guid, JobRecord> _jobs = new();

    public void Add(JobRecord job)
    {
        _jobs[job.Id] = job;
    }

    public JobRecord? Get(Guid id)
    {
        return _jobs.GetValueOrDefault(id);
    }
}
