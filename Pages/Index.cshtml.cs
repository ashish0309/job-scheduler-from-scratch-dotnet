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
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FailedAt,
    int AttemptCount,
    int MaxAttempts,
    bool RetryAvailable,
    string StatusUrl,
    string? FailureReason)
{
    public static JobSummary From(JobRecord job)
    {
        return new JobSummary(
            job.Id,
            job.Type,
            job.Status,
            job.EnqueuedAt,
            job.StartedAt,
            job.CompletedAt,
            job.FailedAt,
            job.AttemptCount,
            job.MaxAttempts,
            job.RetryAvailable,
            $"/api/jobs/{job.Id}",
            job.FailureReason);
    }
}
