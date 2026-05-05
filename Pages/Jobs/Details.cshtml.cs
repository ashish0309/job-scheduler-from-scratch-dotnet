using JobSchedulerPrototype.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JobSchedulerPrototype.Pages.Jobs;

public class DetailsModel : PageModel
{
    private readonly IJobStore _jobs;

    public DetailsModel(IJobStore jobs)
    {
        _jobs = jobs;
    }

    public JobDetails? Job { get; private set; }

    public IActionResult OnGet(Guid id)
    {
        var job = _jobs.Get(id);
        if (job is null)
        {
            return NotFound();
        }

        Job = JobDetails.From(job);
        return Page();
    }
}

public sealed record JobDetails(
    Guid Id,
    string Type,
    JobStatus Status,
    Guid CurrentStateChangeId,
    string? ClaimedBy,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? LeaseExpiresAt,
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
    string? FailureReason,
    string Payload)
{
    public static JobDetails From(JobRecord job)
    {
        return new JobDetails(
            job.Id,
            job.Type,
            job.Status,
            job.CurrentStateChangeId,
            job.ClaimedBy,
            job.ClaimedAt,
            job.LeaseExpiresAt,
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
            job.FailureReason,
            job.Payload.GetRawText());
    }
}
