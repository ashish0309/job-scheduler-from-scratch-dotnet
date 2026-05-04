namespace JobSchedulerPrototype.Jobs;

public sealed record JobQueueEntry(
    DateTimeOffset RunAt,
    DateTimeOffset AddedAt,
    Guid JobId) : IComparable<JobQueueEntry>
{
    public static JobQueueEntry From(JobRecord job, DateTimeOffset addedAt)
    {
        return new JobQueueEntry(
            job.ScheduledAt ?? job.EnqueuedAt,
            addedAt,
            job.Id);
    }

    public int CompareTo(JobQueueEntry? other)
    {
        if (other is null)
        {
            return 1;
        }

        var runAtComparison = RunAt.CompareTo(other.RunAt);
        if (runAtComparison != 0)
        {
            return runAtComparison;
        }

        var addedAtComparison = AddedAt.CompareTo(other.AddedAt);
        if (addedAtComparison != 0)
        {
            return addedAtComparison;
        }

        return JobId.CompareTo(other.JobId);
    }
}
