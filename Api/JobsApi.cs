using System.Text.Json;
using JobSchedulerPrototype.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace JobSchedulerPrototype.Api;

public static class JobsApi
{
    public static RouteGroupBuilder MapJobsApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/jobs");

        var endpointDefinitions = endpoints.ServiceProvider
            .GetServices<IJobEndpointDefinition>()
            .OrderBy(static definition => definition.GetType().FullName, StringComparer.Ordinal);

        foreach (var endpointDefinition in endpointDefinitions)
        {
            endpointDefinition.Map(group);
        }

        return group;
    }

    internal static JobResponse ToResponse(JobRecord job)
    {
        return new JobResponse(
            job.Id,
            job.TenantId,
            job.CreatedByActorId,
            job.Type,
            job.Status.ToString(),
            job.CurrentStateChangeId,
            job.FailureReason,
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
    string TenantId,
    string CreatedByActorId,
    string Type,
    string Status,
    Guid CurrentStateChangeId,
    string? FailureReason,
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
