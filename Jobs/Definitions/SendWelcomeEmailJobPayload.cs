using System.Text.Json.Serialization;

namespace JobSchedulerPrototype.Jobs;

public sealed record SendWelcomeEmailJobPayload(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("shouldFail")] bool ShouldFail = false);
