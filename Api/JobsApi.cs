using System.Text.Json;
using JobSchedulerPrototype.Jobs;
using Microsoft.AspNetCore.Http.HttpResults;

namespace JobSchedulerPrototype.Api;

public static class JobsApi
{
    private const int ImmediateDelaySeconds = 0;

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
        IJobStore jobs,
        IJobDefinitionRegistry definitions)
    {
        var validationResult = Validate(request, definitions);
        if (!validationResult.IsValid)
        {
            return TypedResults.BadRequest(new JobValidationError(validationResult.ErrorMessage));
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var job = validationResult.DelaySeconds > ImmediateDelaySeconds
            ? JobRecord.Schedule(
                id,
                request.Type,
                validationResult.Payload,
                validationResult.RetryPolicy.MaxAttempts,
                now.AddSeconds(validationResult.DelaySeconds),
                now)
            : JobRecord.Enqueue(
                id,
                request.Type,
                validationResult.Payload,
                validationResult.RetryPolicy.MaxAttempts,
                now);

        jobs.Add(job);

        var response = ToResponse(job);
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

    private static EnqueueJobValidationResult Validate(
        EnqueueJobRequest request,
        IJobDefinitionRegistry definitions)
    {
        if (string.IsNullOrWhiteSpace(request.Type))
        {
            return EnqueueJobValidationResult.Invalid("Job type is required.");
        }

        var definition = definitions.Find(request.Type);
        if (definition is null)
        {
            return EnqueueJobValidationResult.Invalid("Unsupported job type.");
        }

        var payloadValidation = definition.ValidatePayload(request.Payload);
        if (!payloadValidation.IsValid)
        {
            return EnqueueJobValidationResult.Invalid(payloadValidation.ErrorMessage ?? "Job payload is invalid.");
        }

        var delaySeconds = request.DelaySeconds ?? ImmediateDelaySeconds;
        if (delaySeconds < ImmediateDelaySeconds)
        {
            return EnqueueJobValidationResult.Invalid("Delay seconds cannot be negative.");
        }

        if (delaySeconds > definition.MaxScheduleDelaySeconds)
        {
            return EnqueueJobValidationResult.Invalid("Delay seconds exceeds the maximum allowed for this job type.");
        }

        return EnqueueJobValidationResult.Valid(
            payloadValidation.Payload,
            definition.RetryPolicy,
            delaySeconds);
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

internal sealed record EnqueueJobValidationResult(
    bool IsValid,
    JsonElement Payload,
    JobRetryPolicy RetryPolicy,
    int DelaySeconds,
    string ErrorMessage)
{
    public static EnqueueJobValidationResult Valid(
        JsonElement payload,
        JobRetryPolicy retryPolicy,
        int delaySeconds)
    {
        return new EnqueueJobValidationResult(
            true,
            payload.Clone(),
            retryPolicy,
            delaySeconds,
            ErrorMessage: string.Empty);
    }

    public static EnqueueJobValidationResult Invalid(string errorMessage)
    {
        return new EnqueueJobValidationResult(
            false,
            default,
            RetryPolicy: JobRetryPolicy.Create(maxAttempts: 1, TimeSpan.Zero),
            DelaySeconds: 0,
            errorMessage);
    }
}
