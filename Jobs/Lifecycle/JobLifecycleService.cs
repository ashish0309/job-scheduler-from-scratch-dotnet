using System.Text.Json;

namespace JobSchedulerPrototype.Jobs;

public sealed class JobLifecycleService : IJobLifecycleService
{
    private const int ImmediateDelaySeconds = 0;

    private readonly IJobStore _jobs;
    private readonly IJobDefinitionRegistry _definitions;

    public JobLifecycleService(
        IJobStore jobs,
        IJobDefinitionRegistry definitions)
    {
        _jobs = jobs;
        _definitions = definitions;
    }

    public JobEnqueueResult Enqueue(
        string type,
        JsonElement payload,
        int? delaySeconds)
    {
        var validationResult = Validate(type, payload, delaySeconds);
        if (!validationResult.IsValid)
        {
            return JobEnqueueResult.Rejected(validationResult.ErrorMessage);
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var job = validationResult.DelaySeconds > ImmediateDelaySeconds
            ? JobRecord.Schedule(
                id,
                type,
                validationResult.Payload,
                validationResult.RetryPolicy.MaxAttempts,
                now.AddSeconds(validationResult.DelaySeconds),
                now)
            : JobRecord.Enqueue(
                id,
                type,
                validationResult.Payload,
                validationResult.RetryPolicy.MaxAttempts,
                now);

        _jobs.Add(job);

        return JobEnqueueResult.Success(job);
    }

    public JobRecord? ClaimNextDueJob(
        DateTimeOffset now,
        string workerId,
        DateTimeOffset leaseExpiresAt)
    {
        return _jobs.TryClaimNextDueJob(now, workerId, leaseExpiresAt);
    }

    public JobExecutionCompletion CompleteExecution(
        JobRecord job,
        JobExecutionResult result)
    {
        var failureReason = result.FailureReason ?? "Job execution failed.";

        if (result.Succeeded)
        {
            _jobs.MarkCompleted(job.Id);
            return JobExecutionCompletion.Completed();
        }

        if (job.AttemptCount < job.MaxAttempts
            && _definitions.Find(job.Type) is { } definition)
        {
            var scheduledAt = DateTimeOffset.UtcNow.Add(definition.RetryPolicy.Delay);
            _jobs.ScheduleRetry(job.Id, failureReason, scheduledAt);
            return JobExecutionCompletion.RetryScheduled(failureReason, scheduledAt);
        }

        _jobs.MarkFailed(job.Id, failureReason);
        return JobExecutionCompletion.Failed(failureReason);
    }

    private JobEnqueueValidationResult Validate(
        string type,
        JsonElement payload,
        int? requestedDelaySeconds)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return JobEnqueueValidationResult.Invalid("Job type is required.");
        }

        var definition = _definitions.Find(type);
        if (definition is null)
        {
            return JobEnqueueValidationResult.Invalid("Unsupported job type.");
        }

        var payloadValidation = definition.ValidatePayload(payload);
        if (!payloadValidation.IsValid)
        {
            return JobEnqueueValidationResult.Invalid(
                payloadValidation.ErrorMessage ?? "Job payload is invalid.");
        }

        var delaySeconds = requestedDelaySeconds ?? ImmediateDelaySeconds;
        if (delaySeconds < ImmediateDelaySeconds)
        {
            return JobEnqueueValidationResult.Invalid("Delay seconds cannot be negative.");
        }

        if (delaySeconds > definition.MaxScheduleDelaySeconds)
        {
            return JobEnqueueValidationResult.Invalid(
                "Delay seconds exceeds the maximum allowed for this job type.");
        }

        return JobEnqueueValidationResult.Valid(
            payloadValidation.Payload,
            definition.RetryPolicy,
            delaySeconds);
    }
}

internal sealed record JobEnqueueValidationResult(
    bool IsValid,
    JsonElement Payload,
    JobRetryPolicy RetryPolicy,
    int DelaySeconds,
    string ErrorMessage)
{
    public static JobEnqueueValidationResult Valid(
        JsonElement payload,
        JobRetryPolicy retryPolicy,
        int delaySeconds)
    {
        return new JobEnqueueValidationResult(
            true,
            payload.Clone(),
            retryPolicy,
            delaySeconds,
            ErrorMessage: string.Empty);
    }

    public static JobEnqueueValidationResult Invalid(string errorMessage)
    {
        return new JobEnqueueValidationResult(
            false,
            default,
            RetryPolicy: JobRetryPolicy.Create(maxAttempts: 1, TimeSpan.Zero),
            DelaySeconds: 0,
            errorMessage);
    }
}
