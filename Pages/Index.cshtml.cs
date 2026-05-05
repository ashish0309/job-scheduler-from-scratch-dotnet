using JobSchedulerPrototype.Jobs;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JobSchedulerPrototype.Pages;

public class IndexModel : PageModel
{
    private readonly IJobStore _jobs;

    public IndexModel(IJobStore jobs)
    {
        _jobs = jobs;
    }

    public IReadOnlyCollection<JobSummary> Jobs { get; private set; } = [];

    public int QueuedCount => Jobs.Count(job => job.Status == JobStatus.Queued);

    public int RunningCount => Jobs.Count(job => job.Status == JobStatus.Running);

    public int CompletedCount => Jobs.Count(job => job.Status == JobStatus.Completed);

    public int FailedCount => Jobs.Count(job => job.Status == JobStatus.Failed);

    public void OnGet()
    {
        Jobs = _jobs.List()
            .Select(JobSummary.From)
            .ToArray();
    }
}

public sealed record JobSummary(
    Guid Id,
    string Type,
    JobStatus Status,
    Guid CurrentStateChangeId,
    string? ClaimedBy,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FailedAt,
    int AttemptCount,
    int MaxAttempts,
    IReadOnlyList<JobAttemptSummary> Attempts,
    IReadOnlyList<JobStateChangeSummary> History,
    string StatusUrl,
    string? FailureReason)
{
    public static JobSummary From(JobRecord job)
    {
        return new JobSummary(
            job.Id,
            job.Type,
            job.Status,
            job.CurrentStateChangeId,
            job.ClaimedBy,
            job.ClaimedAt,
            job.EnqueuedAt,
            job.ScheduledAt,
            job.StartedAt,
            job.CompletedAt,
            job.FailedAt,
            job.AttemptCount,
            job.MaxAttempts,
            job.Attempts.Select(JobAttemptSummary.From).ToArray(),
            job.History.Select(JobStateChangeSummary.From).ToArray(),
            $"/api/jobs/{job.Id}",
            job.FailureReason);
    }
}

public sealed record JobAttemptSummary(
    int Number,
    JobStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FailedAt,
    TimeSpan? Duration,
    string? FailureReason)
{
    public static JobAttemptSummary From(JobAttempt attempt)
    {
        return new JobAttemptSummary(
            attempt.Number,
            attempt.Status,
            attempt.StartedAt,
            attempt.CompletedAt,
            attempt.FailedAt,
            attempt.Duration,
            attempt.FailureReason);
    }
}

public sealed record JobStateChangeSummary(
    Guid Id,
    JobStatus Status,
    DateTimeOffset ChangedAt,
    string Reason,
    DateTimeOffset? ScheduledAt)
{
    public static JobStateChangeSummary From(JobStateChange stateChange)
    {
        return new JobStateChangeSummary(
            stateChange.Id,
            stateChange.Status,
            stateChange.ChangedAt,
            stateChange.Reason,
            stateChange.ScheduledAt);
    }
}
