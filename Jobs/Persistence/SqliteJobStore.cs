using Microsoft.EntityFrameworkCore;

namespace JobSchedulerPrototype.Jobs;

public sealed class SqliteJobStore : IJobStore
{
    private readonly IDbContextFactory<JobSchedulerDbContext> _dbContextFactory;

    public SqliteJobStore(IDbContextFactory<JobSchedulerDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public void Add(JobRecord job)
    {
        using var db = _dbContextFactory.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();

        db.Jobs.Add(job);
        db.SaveChanges();

        transaction.Commit();
    }

    public JobRecord? Get(Guid id)
    {
        using var db = _dbContextFactory.CreateDbContext();

        return LoadJob(db, id);
    }

    public IReadOnlyCollection<JobRecord> List()
    {
        using var db = _dbContextFactory.CreateDbContext();

        return LoadJobs(db)
            .OrderBy(job => job.EnqueuedAt)
            .ToArray();
    }

    public JobRecord? TryClaimNextDueJob(DateTimeOffset now)
    {
        using var db = _dbContextFactory.CreateDbContext();

        var job = LoadJobs(db)
            .Where(job => job.Status is JobStatus.Queued or JobStatus.Scheduled)
            .Select(job => new
            {
                Job = job,
                RunAt = job.ScheduledAt ?? job.EnqueuedAt
            })
            .Where(candidate => candidate.RunAt <= now)
            .OrderBy(candidate => candidate.RunAt)
            .ThenBy(candidate => candidate.Job.EnqueuedAt)
            .ThenBy(candidate => candidate.Job.Id)
            .Select(candidate => candidate.Job)
            .FirstOrDefault();

        if (job is null)
        {
            return null;
        }

        if (job.Status == JobStatus.Scheduled)
        {
            job = job.TransitionTo(JobStatus.Queued, now);
        }

        if (job.Status != JobStatus.Queued)
        {
            throw new InvalidOperationException(
                $"Pending job query returned non-runnable job '{job.Id}' with status '{job.Status}'.");
        }

        var runningJob = job.TransitionTo(JobStatus.Running, now);
        ReplaceJob(db, runningJob);
        return runningJob;
    }

    public bool MarkCompleted(Guid id)
    {
        using var db = _dbContextFactory.CreateDbContext();

        var job = LoadJob(db, id);
        if (job is null || job.Status != JobStatus.Running)
        {
            return false;
        }

        ReplaceJob(db, job.TransitionTo(JobStatus.Completed, DateTimeOffset.UtcNow));
        return true;
    }

    public bool MarkFailed(Guid id, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        using var db = _dbContextFactory.CreateDbContext();

        var job = LoadJob(db, id);
        if (job is null || job.Status != JobStatus.Running)
        {
            return false;
        }

        ReplaceJob(db, job.TransitionToFailed(reason, DateTimeOffset.UtcNow));
        return true;
    }

    public bool ScheduleRetry(Guid id, string reason, DateTimeOffset scheduledAt)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        using var db = _dbContextFactory.CreateDbContext();

        var job = LoadJob(db, id);
        if (job is null
            || job.Status != JobStatus.Running
            || job.AttemptCount >= job.MaxAttempts)
        {
            return false;
        }

        ReplaceJob(db, job.ScheduleRetry(reason, DateTimeOffset.UtcNow, scheduledAt));
        return true;
    }

    private static JobRecord? LoadJob(JobSchedulerDbContext db, Guid id)
    {
        return db.Jobs
            .AsNoTracking()
            .Include(job => job.History)
            .SingleOrDefault(job => job.Id == id)
            ?.WithOrderedHistory();
    }

    private static IEnumerable<JobRecord> LoadJobs(JobSchedulerDbContext db)
    {
        return db.Jobs
            .AsNoTracking()
            .Include(job => job.History)
            .AsEnumerable()
            .Select(job => job.WithOrderedHistory());
    }

    private static void ReplaceJob(JobSchedulerDbContext db, JobRecord job)
    {
        using var transaction = db.Database.BeginTransaction();

        var existingJob = db.Jobs
            .Include(entity => entity.History)
            .SingleOrDefault(entity => entity.Id == job.Id);

        if (existingJob is null)
        {
            return;
        }

        db.Jobs.Remove(existingJob);
        db.SaveChanges();

        db.Jobs.Add(job);
        db.SaveChanges();

        transaction.Commit();
    }
}
