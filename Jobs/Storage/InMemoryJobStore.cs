namespace JobSchedulerPrototype.Jobs;

public sealed class InMemoryJobStore : IJobStore
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, JobRecord> _jobsById = new();
    private readonly SortedSet<JobQueueEntry> _pendingJobs = [];

    public void Add(JobRecord job)
    {
        lock (_lock)
        {
            _jobsById[job.Id] = job;
            AddPendingJobUnderLock(job, DateTimeOffset.UtcNow);
        }
    }

    public JobRecord? Get(Guid id)
    {
        lock (_lock)
        {
            return _jobsById.GetValueOrDefault(id);
        }
    }

    public IReadOnlyCollection<JobRecord> List()
    {
        JobRecord[] jobs;

        lock (_lock)
        {
            jobs = _jobsById.Values.ToArray();
        }

        return jobs
            .OrderBy(job => job.EnqueuedAt)
            .ToArray();
    }

    public JobRecord? TryClaimNextDueJob(DateTimeOffset now)
    {
        lock (_lock)
        {
            while (_pendingJobs.Min is { } nextJob)
            {
                if (nextJob.RunAt > now)
                {
                    return null;
                }

                _pendingJobs.Remove(nextJob);

                if (!_jobsById.TryGetValue(nextJob.JobId, out var job))
                {
                    throw new InvalidOperationException(
                        $"Pending job index contains missing job '{nextJob.JobId}'.");
                }

                if (job.Status == JobStatus.Scheduled)
                {
                    job = job.TransitionTo(JobStatus.Queued, now);
                }

                if (job.Status != JobStatus.Queued)
                {
                    throw new InvalidOperationException(
                        $"Pending job index contains non-runnable job '{job.Id}' with status '{job.Status}'.");
                }

                var runningJob = job.TransitionTo(JobStatus.Running, now);
                _jobsById[job.Id] = runningJob;
                return runningJob;
            }
        }

        return null;
    }

    public bool MarkCompleted(Guid id)
    {
        lock (_lock)
        {
            if (!_jobsById.TryGetValue(id, out var job) || job.Status != JobStatus.Running)
            {
                return false;
            }

            _jobsById[id] = job.TransitionTo(JobStatus.Completed, DateTimeOffset.UtcNow);
            return true;
        }
    }

    public bool MarkFailed(Guid id, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        lock (_lock)
        {
            if (!_jobsById.TryGetValue(id, out var job) || job.Status != JobStatus.Running)
            {
                return false;
            }

            _jobsById[id] = job.TransitionToFailed(reason, DateTimeOffset.UtcNow);
            return true;
        }
    }

    public bool Retry(Guid id)
    {
        lock (_lock)
        {
            if (!_jobsById.TryGetValue(id, out var job) || !job.RetryAvailable)
            {
                return false;
            }

            var retriedJob = job.Retry(DateTimeOffset.UtcNow);
            _jobsById[id] = retriedJob;
            AddPendingJobUnderLock(retriedJob, DateTimeOffset.UtcNow);
            return true;
        }
    }

    private void AddPendingJobUnderLock(JobRecord job, DateTimeOffset addedAt)
    {
        if (job.Status is not (JobStatus.Queued or JobStatus.Scheduled))
        {
            return;
        }

        _pendingJobs.Add(JobQueueEntry.From(job, addedAt));
    }
}
