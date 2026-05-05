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

    public JobRecord? TryClaimNextDueJob(
        DateTimeOffset now,
        string workerId,
        DateTimeOffset leaseExpiresAt)
    {
        lock (_lock)
        {
            while (NextDuePendingOrExpiredRunningJob(now) is { } nextJob)
            {
                if (!_jobsById.TryGetValue(nextJob.JobId, out var job))
                {
                    throw new InvalidOperationException(
                        $"Pending job index contains missing job '{nextJob.JobId}'.");
                }

                if (job.Status == JobStatus.Running)
                {
                    var reclaimedJob = job.ReclaimExpiredLease(workerId, now, leaseExpiresAt);
                    _jobsById[job.Id] = reclaimedJob;
                    return reclaimedJob;
                }

                _pendingJobs.Remove(nextJob);

                if (job.Status == JobStatus.Scheduled)
                {
                    job = job.TransitionTo(JobStatus.Queued, now);
                }

                if (job.Status != JobStatus.Queued)
                {
                    throw new InvalidOperationException(
                        $"Pending job index contains non-runnable job '{job.Id}' with status '{job.Status}'.");
                }

                var runningJob = job.Claim(workerId, now, leaseExpiresAt);
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

    public bool ScheduleRetry(Guid id, string reason, DateTimeOffset scheduledAt)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        lock (_lock)
        {
            if (!_jobsById.TryGetValue(id, out var job)
                || job.Status != JobStatus.Running
                || job.AttemptCount >= job.MaxAttempts)
            {
                return false;
            }

            var retriedJob = job.ScheduleRetry(reason, DateTimeOffset.UtcNow, scheduledAt);
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

    private JobQueueEntry? NextDuePendingOrExpiredRunningJob(DateTimeOffset now)
    {
        var nextPendingJob = _pendingJobs.Min is { } pendingJob && pendingJob.RunAt <= now
            ? pendingJob
            : null;
        var nextExpiredRunningJob = _jobsById.Values
            .Where(job => job.Status == JobStatus.Running
                && job.LeaseExpiresAt is not null
                && job.LeaseExpiresAt <= now)
            .OrderBy(job => job.LeaseExpiresAt)
            .ThenBy(job => job.Id)
            .Select(job => new JobQueueEntry(job.LeaseExpiresAt!.Value, job.ClaimedAt ?? job.EnqueuedAt, job.Id))
            .FirstOrDefault();

        if (nextPendingJob is null)
        {
            return nextExpiredRunningJob;
        }

        if (nextExpiredRunningJob is null)
        {
            return nextPendingJob;
        }

        return nextPendingJob.CompareTo(nextExpiredRunningJob) <= 0
            ? nextPendingJob
            : nextExpiredRunningJob;
    }
}
