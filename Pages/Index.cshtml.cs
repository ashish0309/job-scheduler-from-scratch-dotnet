using JobSchedulerPrototype.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JobSchedulerPrototype.Pages;

public class IndexModel : PageModel
{
    private readonly IJobActionDispatcher _actions;

    public IndexModel(IJobActionDispatcher actions)
    {
        _actions = actions;
    }

    public IReadOnlyCollection<JobSummary> Jobs { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? StatusLevel { get; set; }

    public int QueuedCount => Jobs.Count(job => job.Status == JobStatus.Queued);

    public int RunningCount => Jobs.Count(job => job.Status == JobStatus.Running);

    public int CompletedCount => Jobs.Count(job => job.Status == JobStatus.Completed);

    public int FailedCount => Jobs.Count(job => job.Status == JobStatus.Failed);

    public async Task OnGet(CancellationToken cancellationToken)
    {
        var result = await _actions.DispatchAsync(
            new ListJobsActionRequest(),
            cancellationToken);

        Jobs = result.IsAuthorized
            ? result.Jobs.Select(JobSummary.From).ToArray()
            : [];
    }

    public async Task<IActionResult> OnPostAcknowledge(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            StatusLevel = "warning";
            StatusMessage = "Job ID is required.";
            return RedirectToPage();
        }

        var result = await _actions.DispatchAsync(
            new AcknowledgeJobActionRequest(id),
            cancellationToken);

        if (!result.IsAuthorized)
        {
            StatusLevel = "danger";
            StatusMessage = result.ErrorMessage ?? "You are not authorized to acknowledge jobs.";
            return RedirectToPage();
        }

        if (!result.Acknowledged)
        {
            StatusLevel = "warning";
            StatusMessage = "Job could not be acknowledged. It may not exist or may be outside your scope.";
            return RedirectToPage();
        }

        StatusLevel = "success";
        StatusMessage = $"Acknowledged job {id}.";
        return RedirectToPage();
    }
}

public sealed record JobSummary(
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
    string? FailureReason)
{
    public static JobSummary From(JobRecord job)
    {
        return new JobSummary(
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
