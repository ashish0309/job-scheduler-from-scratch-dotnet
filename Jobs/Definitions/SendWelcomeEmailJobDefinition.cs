using System.Text.Json;

namespace JobSchedulerPrototype.Jobs;

public sealed class SendWelcomeEmailJobDefinition : IJobDefinition
{
    public string Type => JobTypes.SendWelcomeEmail;

    public JobRetryPolicy RetryPolicy { get; } = JobRetryPolicy.Create(
        maxAttempts: 3,
        delay: TimeSpan.FromSeconds(10));

    public int MaxScheduleDelaySeconds => 3600;

    public JobPayloadValidationResult ValidatePayload(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return JobPayloadValidationResult.Invalid("Job payload is required.");
        }

        SendWelcomeEmailJobPayload? typedPayload;
        try
        {
            typedPayload = payload.Deserialize<SendWelcomeEmailJobPayload>();
        }
        catch (JsonException)
        {
            return JobPayloadValidationResult.Invalid("Job payload is invalid.");
        }

        if (typedPayload is null)
        {
            return JobPayloadValidationResult.Invalid("Job payload is invalid.");
        }

        if (string.IsNullOrWhiteSpace(typedPayload.UserId))
        {
            return JobPayloadValidationResult.Invalid("User ID is required.");
        }

        if (string.IsNullOrWhiteSpace(typedPayload.Email))
        {
            return JobPayloadValidationResult.Invalid("Email is required.");
        }

        return JobPayloadValidationResult.Valid(JsonSerializer.SerializeToElement(typedPayload));
    }
}
