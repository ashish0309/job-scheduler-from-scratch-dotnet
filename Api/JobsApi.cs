using System.Text.Json;
using JobSchedulerPrototype.Jobs;
using Microsoft.AspNetCore.Http.HttpResults;

namespace JobSchedulerPrototype.Api;

public static class JobsApi
{
    public static RouteGroupBuilder MapJobsApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/jobs");

        group.MapPost("", EnqueueJob);
        group.MapGet("", ListJobs);
        group.MapGet("/{id:guid}", GetJob);

        return group;
    }

    private static Results<Accepted<JobResponse>, BadRequest<JobValidationError>> EnqueueJob(
        EnqueueJobRequest request,
        IJobLifecycleService lifecycle)
    {
        var result = lifecycle.Enqueue(
            request.Type,
            request.Payload,
            request.DelaySeconds);

        if (!result.Accepted)
        {
            return TypedResults.BadRequest(new JobValidationError(
                result.ErrorMessage ?? "Job request is invalid."));
        }

        var response = ToResponse(result.Job!);
        return TypedResults.Accepted(response.StatusUrl, response);
    }

    private static Ok<IReadOnlyCollection<JobResponse>> ListJobs(IJobStore jobs)
    {
        IReadOnlyCollection<JobResponse> response = jobs.List()
            .Select(ToResponse)
            .ToArray();

        return TypedResults.Ok(response);
    }

    private static Results<Ok<JobResponse>, NotFound> GetJob(
        Guid id,
        IJobStore jobs)
    {
        var job = jobs.Get(id);

        return job is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ToResponse(job));
    }

    private static JobResponse ToResponse(JobRecord job)
    {
        return new JobResponse(
            job.Id,
            job.Type,
            job.Status.ToString(),
            job.CurrentStateChangeId,
            job.FailureReason,
            job.EnqueuedAt,
            job.ScheduledAt,
            job.StartedAt,
            job.CompletedAt,
            job.FailedAt,
            job.AttemptCount,
            job.MaxAttempts,
            job.Attempts.Select(ToResponse).ToArray(),
            job.History.Select(ToResponse).ToArray(),
            $"/api/jobs/{job.Id}");
    }

    private static JobAttemptResponse ToResponse(JobAttempt attempt)
    {
        return new JobAttemptResponse(
            attempt.Number,
            attempt.Status.ToString(),
            attempt.StartedAt,
            attempt.CompletedAt,
            attempt.FailedAt,
            attempt.Duration,
            attempt.FailureReason);
    }

    private static JobStateChangeResponse ToResponse(JobStateChange stateChange)
    {
        return new JobStateChangeResponse(
            stateChange.Id,
            stateChange.Status.ToString(),
            stateChange.ChangedAt,
            stateChange.Reason,
            stateChange.ScheduledAt);
    }
}

public sealed record EnqueueJobRequest(
    string Type,
    JsonElement Payload,
    int? DelaySeconds = null);

public sealed record JobResponse(
    Guid Id,
    string Type,
    string Status,
    Guid CurrentStateChangeId,
    string? FailureReason,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FailedAt,
    int AttemptCount,
    int MaxAttempts,
    IReadOnlyCollection<JobAttemptResponse> Attempts,
    IReadOnlyCollection<JobStateChangeResponse> History,
    string StatusUrl);

public sealed record JobAttemptResponse(
    int Number,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FailedAt,
    TimeSpan? Duration,
    string? FailureReason);

public sealed record JobStateChangeResponse(
    Guid Id,
    string Status,
    DateTimeOffset ChangedAt,
    string Reason,
    DateTimeOffset? ScheduledAt);

public sealed record JobValidationError(string Message);
