using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;

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

    public JobRecord? TryClaimNextDueJob(
        DateTimeOffset now,
        string workerId,
        DateTimeOffset leaseExpiresAt)
    {
        using var db = _dbContextFactory.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();

        var jobId = db.Jobs
            .Where(job =>
                (job.RunAt != null
                    && job.RunAt <= now
                    && (job.Status == JobStatus.Queued || job.Status == JobStatus.Scheduled))
                || (job.Status == JobStatus.Running
                    && job.LeaseExpiresAt != null
                    && job.LeaseExpiresAt <= now))
            .OrderBy(job => job.Status == JobStatus.Running ? job.LeaseExpiresAt : job.RunAt)
            .ThenBy(job => job.Id)
            .Select(job => (Guid?)job.Id)
            .FirstOrDefault();

        if (jobId is null)
        {
            return null;
        }

        var job = LoadJob(db, jobId.Value);
        if (job is null)
        {
            throw new InvalidOperationException(
                $"Due job query returned missing job '{jobId}'.");
        }

        var originalStatus = job.Status;
        var originalCurrentStateChangeId = job.CurrentStateChangeId;
        var originalHistoryCount = job.History.Count;

        if (job.Status == JobStatus.Running)
        {
            var reclaimedJob = job.ReclaimExpiredLease(workerId, now, leaseExpiresAt);
            var reclaimRowsUpdated = db.Jobs
                .Where(existingJob => existingJob.Id == reclaimedJob.Id
                    && existingJob.Status == JobStatus.Running
                    && existingJob.CurrentStateChangeId == originalCurrentStateChangeId)
                .ExecuteUpdate(setters => setters
                    .SetProperty(existingJob => existingJob.CurrentStateChangeId, reclaimedJob.CurrentStateChangeId)
                    .SetProperty(existingJob => existingJob.ClaimedBy, reclaimedJob.ClaimedBy)
                    .SetProperty(existingJob => existingJob.ClaimedAt, reclaimedJob.ClaimedAt)
                    .SetProperty(existingJob => existingJob.LeaseExpiresAt, reclaimedJob.LeaseExpiresAt));

            if (reclaimRowsUpdated == 0)
            {
                transaction.Rollback();
                return null;
            }

            AppendStateChanges(
                db,
                reclaimedJob.Id,
                reclaimedJob.History.Skip(originalHistoryCount));

            db.SaveChanges();
            transaction.Commit();

            return LoadJob(db, reclaimedJob.Id);
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

        var runningJob = job.Claim(workerId, now, leaseExpiresAt);
        var rowsUpdated = db.Jobs
            .Where(existingJob => existingJob.Id == runningJob.Id
                && existingJob.Status == originalStatus
                && existingJob.CurrentStateChangeId == originalCurrentStateChangeId)
            .ExecuteUpdate(setters => setters
                .SetProperty(existingJob => existingJob.Status, JobStatus.Running)
                .SetProperty(existingJob => existingJob.CurrentStateChangeId, runningJob.CurrentStateChangeId)
                .SetProperty(existingJob => existingJob.RunAt, runningJob.RunAt)
                .SetProperty(existingJob => existingJob.ClaimedBy, runningJob.ClaimedBy)
                .SetProperty(existingJob => existingJob.ClaimedAt, runningJob.ClaimedAt)
                .SetProperty(existingJob => existingJob.LeaseExpiresAt, runningJob.LeaseExpiresAt));

        if (rowsUpdated == 0)
        {
            transaction.Rollback();
            return null;
        }

        AppendStateChanges(
            db,
            runningJob.Id,
            runningJob.History.Skip(originalHistoryCount));

        db.SaveChanges();
        transaction.Commit();

        return LoadJob(db, runningJob.Id);
    }

    public bool MarkCompleted(Guid id, Guid expectedCurrentStateChangeId)
    {
        using var db = _dbContextFactory.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();

        var job = LoadJob(db, id);
        if (job is null || job.Status != JobStatus.Running)
        {
            return false;
        }

        var completedJob = job.TransitionTo(JobStatus.Completed, DateTimeOffset.UtcNow);
        var completed = TryPersistTransition(
            db,
            id,
            expectedStatus: JobStatus.Running,
            expectedCurrentStateChangeId,
            completedJob,
            previousHistoryCount: job.History.Count,
            setters => setters
                .SetProperty(existingJob => existingJob.Status, JobStatus.Completed)
                .SetProperty(existingJob => existingJob.CurrentStateChangeId, completedJob.CurrentStateChangeId)
                .SetProperty(existingJob => existingJob.RunAt, completedJob.RunAt)
                .SetProperty(existingJob => existingJob.ClaimedBy, completedJob.ClaimedBy)
                .SetProperty(existingJob => existingJob.ClaimedAt, completedJob.ClaimedAt)
                .SetProperty(existingJob => existingJob.LeaseExpiresAt, completedJob.LeaseExpiresAt));

        if (!completed)
        {
            transaction.Rollback();
            return false;
        }

        transaction.Commit();
        return true;
    }

    public bool MarkFailed(Guid id, Guid expectedCurrentStateChangeId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        using var db = _dbContextFactory.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();

        var job = LoadJob(db, id);
        if (job is null || job.Status != JobStatus.Running)
        {
            return false;
        }

        var failedJob = job.TransitionToFailed(reason, DateTimeOffset.UtcNow);
        var failed = TryPersistTransition(
            db,
            id,
            expectedStatus: JobStatus.Running,
            expectedCurrentStateChangeId,
            failedJob,
            previousHistoryCount: job.History.Count,
            setters => setters
                .SetProperty(existingJob => existingJob.Status, JobStatus.Failed)
                .SetProperty(existingJob => existingJob.CurrentStateChangeId, failedJob.CurrentStateChangeId)
                .SetProperty(existingJob => existingJob.RunAt, failedJob.RunAt)
                .SetProperty(existingJob => existingJob.ClaimedBy, failedJob.ClaimedBy)
                .SetProperty(existingJob => existingJob.ClaimedAt, failedJob.ClaimedAt)
                .SetProperty(existingJob => existingJob.LeaseExpiresAt, failedJob.LeaseExpiresAt)
                .SetProperty(existingJob => existingJob.FailureReason, failedJob.FailureReason));

        if (!failed)
        {
            transaction.Rollback();
            return false;
        }

        transaction.Commit();
        return true;
    }

    public bool ScheduleRetry(
        Guid id,
        Guid expectedCurrentStateChangeId,
        string reason,
        DateTimeOffset scheduledAt)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        using var db = _dbContextFactory.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();

        var job = LoadJob(db, id);
        if (job is null
            || job.Status != JobStatus.Running
            || job.AttemptCount >= job.MaxAttempts)
        {
            return false;
        }

        var retriedJob = job.ScheduleRetry(reason, DateTimeOffset.UtcNow, scheduledAt);
        var scheduled = TryPersistTransition(
            db,
            id,
            expectedStatus: JobStatus.Running,
            expectedCurrentStateChangeId,
            retriedJob,
            previousHistoryCount: job.History.Count,
            setters => setters
                .SetProperty(existingJob => existingJob.Status, JobStatus.Scheduled)
                .SetProperty(existingJob => existingJob.CurrentStateChangeId, retriedJob.CurrentStateChangeId)
                .SetProperty(existingJob => existingJob.RunAt, retriedJob.RunAt)
                .SetProperty(existingJob => existingJob.ClaimedBy, retriedJob.ClaimedBy)
                .SetProperty(existingJob => existingJob.ClaimedAt, retriedJob.ClaimedAt)
                .SetProperty(existingJob => existingJob.LeaseExpiresAt, retriedJob.LeaseExpiresAt)
                .SetProperty(existingJob => existingJob.FailureReason, retriedJob.FailureReason));

        if (!scheduled)
        {
            transaction.Rollback();
            return false;
        }

        transaction.Commit();
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

    private static void AppendStateChanges(
        JobSchedulerDbContext db,
        Guid jobId,
        IEnumerable<JobStateChange> stateChanges)
    {
        foreach (var stateChange in stateChanges)
        {
            db.JobStateChanges.Add(stateChange);
            db.Entry(stateChange).Property("JobId").CurrentValue = jobId;
        }
    }

    private static bool TryPersistTransition(
        JobSchedulerDbContext db,
        Guid jobId,
        JobStatus expectedStatus,
        Guid expectedCurrentStateChangeId,
        JobRecord updatedJob,
        int previousHistoryCount,
        Expression<Func<SetPropertyCalls<JobRecord>, SetPropertyCalls<JobRecord>>> setProperties)
    {
        var rowsUpdated = db.Jobs
            .Where(existingJob => existingJob.Id == jobId
                && existingJob.Status == expectedStatus
                && existingJob.CurrentStateChangeId == expectedCurrentStateChangeId)
            .ExecuteUpdate(setProperties);

        if (rowsUpdated == 0)
        {
            return false;
        }

        AppendStateChanges(
            db,
            jobId,
            updatedJob.History.Skip(previousHistoryCount));
        db.SaveChanges();
        return true;
    }
}
