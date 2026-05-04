using System.Text.Json;
using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class SendWelcomeEmailJobDefinitionTests
{
    [Fact]
    public void ValidatePayloadReturnsNormalizedPayloadWhenPayloadIsValid()
    {
        var definition = new SendWelcomeEmailJobDefinition();

        var result = definition.ValidatePayload(Payload(
            """{"userId":"user_123","email":"person@example.com","shouldFail":true}"""));

        Assert.Equal(3, definition.DefaultMaxAttempts);
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);

        var payload = result.Payload.Deserialize<SendWelcomeEmailJobPayload>();
        Assert.NotNull(payload);
        Assert.Equal("user_123", payload.UserId);
        Assert.Equal("person@example.com", payload.Email);
        Assert.True(payload.ShouldFail);
    }

    [Theory]
    [InlineData("""{"email":"person@example.com"}""", "User ID is required.")]
    [InlineData("""{"userId":"user_123"}""", "Email is required.")]
    [InlineData("""{"userId":"","email":"person@example.com"}""", "User ID is required.")]
    [InlineData("""{"userId":"user_123","email":""}""", "Email is required.")]
    public void ValidatePayloadRejectsInvalidPayloads(string json, string expectedMessage)
    {
        var definition = new SendWelcomeEmailJobDefinition();

        var result = definition.ValidatePayload(Payload(json));

        Assert.False(result.IsValid);
        Assert.Equal(expectedMessage, result.ErrorMessage);
    }

    private static JsonElement Payload(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
