using System.Text.Json;
using JobSchedulerPrototype.Jobs;
using Microsoft.AspNetCore.Http.HttpResults;

namespace JobSchedulerPrototype.Api;

public static class JobsApi
{
    private const string QueuedStatus = "Queued";
    private static readonly HashSet<string> SupportedJobTypes = new(StringComparer.Ordinal)
    {
        "send-welcome-email"
    };

    public static RouteGroupBuilder MapJobsApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/jobs");

        group.MapPost("", EnqueueJob);
        group.MapGet("/{id:guid}", GetJob);

        return group;
    }

    private static Results<Accepted<JobResponse>, BadRequest<JobValidationError>> EnqueueJob(
        EnqueueJobRequest request,
        IJobStore jobs)
    {
        var validationError = Validate(request);
        if (validationError is not null)
        {
            return TypedResults.BadRequest(validationError);
        }

        var job = new JobRecord(
            Guid.NewGuid(),
            request.Type,
            request.Payload.Clone(),
            QueuedStatus,
            DateTimeOffset.UtcNow);

        jobs.Add(job);

        var response = ToResponse(job);
        return TypedResults.Accepted(response.StatusUrl, response);
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

    private static JobValidationError? Validate(EnqueueJobRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Type))
        {
            return new JobValidationError("Job type is required.");
        }

        if (!SupportedJobTypes.Contains(request.Type))
        {
            return new JobValidationError("Unsupported job type.");
        }

        if (request.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new JobValidationError("Job payload is required.");
        }

        return null;
    }

    private static JobResponse ToResponse(JobRecord job)
    {
        return new JobResponse(
            job.Id,
            job.Type,
            job.Status,
            job.EnqueuedAt,
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
    DateTimeOffset EnqueuedAt,
    string StatusUrl);

public sealed record JobValidationError(string Message);
