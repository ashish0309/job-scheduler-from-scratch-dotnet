using System.Text.Json;

namespace JobSchedulerPrototype.Jobs;

public interface IJobDefinition
{
    string Type { get; }

    int DefaultMaxAttempts { get; }

    int MaxScheduleDelaySeconds { get; }

    JobPayloadValidationResult ValidatePayload(JsonElement payload);
}

public sealed record JobPayloadValidationResult(
    bool IsValid,
    JsonElement Payload,
    string? ErrorMessage)
{
    public static JobPayloadValidationResult Valid(JsonElement payload)
    {
        return new JobPayloadValidationResult(true, payload.Clone(), ErrorMessage: null);
    }

    public static JobPayloadValidationResult Invalid(string errorMessage)
    {
        return new JobPayloadValidationResult(false, default, errorMessage);
    }
}
