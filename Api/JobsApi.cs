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
        group.MapPost("/{id:guid}/retry", RetryJob);

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

        var job = JobRecord.Enqueue(
            Guid.NewGuid(),
            request.Type,
            validationResult.Payload,
            validationResult.DefaultMaxAttempts,
            DateTimeOffset.UtcNow);

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

    private static Results<Ok<JobResponse>, NotFound, Conflict<JobValidationError>> RetryJob(
        Guid id,
        IJobStore jobs)
    {
        var job = jobs.Get(id);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        if (!jobs.Retry(id))
        {
            return TypedResults.Conflict(new JobValidationError("Job is not eligible for retry."));
        }

        return TypedResults.Ok(ToResponse(jobs.Get(id)!));
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

        return EnqueueJobValidationResult.Valid(
            payloadValidation.Payload,
            definition.DefaultMaxAttempts);
    }

    private static JobResponse ToResponse(JobRecord job)
    {
        return new JobResponse(
            job.Id,
            job.Type,
            job.Status.ToString(),
            job.FailureReason,
            job.EnqueuedAt,
            job.StartedAt,
            job.CompletedAt,
            job.FailedAt,
            job.AttemptCount,
            job.MaxAttempts,
            job.RetryAvailable,
            $"/api/jobs/{job.Id}");
    }
}

public sealed record EnqueueJobRequest(
    string Type,
    JsonElement Payload);

public sealed record JobResponse(
    Guid Id,
    string Type,
    string Status,
    string? FailureReason,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FailedAt,
    int AttemptCount,
    int MaxAttempts,
    bool RetryAvailable,
    string StatusUrl);

public sealed record JobValidationError(string Message);

internal sealed record EnqueueJobValidationResult(
    bool IsValid,
    JsonElement Payload,
    int DefaultMaxAttempts,
    string ErrorMessage)
{
    public static EnqueueJobValidationResult Valid(JsonElement payload, int defaultMaxAttempts)
    {
        return new EnqueueJobValidationResult(
            true,
            payload.Clone(),
            defaultMaxAttempts,
            ErrorMessage: string.Empty);
    }

    public static EnqueueJobValidationResult Invalid(string errorMessage)
    {
        return new EnqueueJobValidationResult(
            false,
            default,
            DefaultMaxAttempts: 0,
            errorMessage);
    }
}
