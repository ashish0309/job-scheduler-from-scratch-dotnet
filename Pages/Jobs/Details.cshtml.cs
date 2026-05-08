using JobSchedulerPrototype.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JobSchedulerPrototype.Pages.Jobs;

public class DetailsModel : PageModel
{
    private readonly IJobActionDispatcher _actions;

    public DetailsModel(IJobActionDispatcher actions)
    {
        _actions = actions;
    }

    public JobDetails? Job { get; private set; }

    public async Task<IActionResult> OnGet(Guid id, CancellationToken cancellationToken)
    {
        var result = await _actions.DispatchAsync(
            new GetJobByIdActionRequest(id),
            cancellationToken);
        if (!result.IsAuthorized)
        {
            return Forbid();
        }

        var job = result.Job;
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
    string TenantId,
    string CreatedByActorId,
    string Type,
    JobStatus Status,
    Guid CurrentStateChangeId,
    string? ClaimedBy,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? LeaseExpiresAt,
    string? AcknowledgedBy,
    DateTimeOffset? AcknowledgedAt,
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
            job.TenantId,
            job.CreatedByActorId,
            job.Type,
            job.Status,
            job.CurrentStateChangeId,
            job.ClaimedBy,
            job.ClaimedAt,
            job.LeaseExpiresAt,
            job.AcknowledgedBy,
            job.AcknowledgedAt,
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
