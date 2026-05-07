using System.Text.Json;

namespace JobSchedulerPrototype.Jobs;

public sealed class EnqueueJobAction : JobAuthorizedAction<EnqueueJobActionRequest, JobEnqueueResult>
{
    private const int ImmediateDelaySeconds = 0;

    private readonly IJobStore _jobs;
    private readonly IJobDefinitionRegistry _definitions;

    public EnqueueJobAction(
        IJobStore jobs,
        IJobDefinitionRegistry definitions,
        IJobActorProvider actorProvider,
        IJobAuthorizationRuleEvaluator ruleEvaluator)
        : base(actorProvider, ruleEvaluator)
    {
        _jobs = jobs;
        _definitions = definitions;
    }

    public Task<JobEnqueueResult> ExecuteAsync(
        string type,
        JsonElement payload,
        int? delaySeconds,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            new EnqueueJobActionRequest(type, payload, delaySeconds),
            cancellationToken);
    }

    protected override IReadOnlyList<IJobAuthorizationRule> BuildAuthorizationRules(EnqueueJobActionRequest request)
    {
        return
        [
            new PermissionJobAuthorizationRule(
                JobPermissions.EmailEnqueue,
                "Actor is not authorized to enqueue jobs.")
        ];
    }

    protected override JobEnqueueResult OnAuthorizationDenied(JobAuthorizationResult result)
    {
        return JobEnqueueResult.Rejected(result.ErrorMessage ?? "Not authorized to enqueue jobs.");
    }

    protected override Task<JobEnqueueResult> ExecuteAuthorizedAsync(
        EnqueueJobActionRequest request,
        JobActor actor,
        CancellationToken cancellationToken)
    {
        var validationResult = Validate(request.Type, request.Payload, request.DelaySeconds);
        if (!validationResult.IsValid)
        {
            return Task.FromResult(JobEnqueueResult.Rejected(validationResult.ErrorMessage));
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var job = validationResult.DelaySeconds > ImmediateDelaySeconds
            ? JobRecord.Schedule(
                id,
                actor.TenantId,
                actor.Id,
                request.Type,
                validationResult.Payload,
                validationResult.RetryPolicy.MaxAttempts,
                now.AddSeconds(validationResult.DelaySeconds),
                now)
            : JobRecord.Enqueue(
                id,
                actor.TenantId,
                actor.Id,
                request.Type,
                validationResult.Payload,
                validationResult.RetryPolicy.MaxAttempts,
                now);

        _jobs.Add(job);

        return Task.FromResult(JobEnqueueResult.Success(job));
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

    private sealed record JobEnqueueValidationResult(
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
}

public sealed record EnqueueJobActionRequest(
    string Type,
    JsonElement Payload,
    int? DelaySeconds) : IJobActionRequest<JobEnqueueResult>;
