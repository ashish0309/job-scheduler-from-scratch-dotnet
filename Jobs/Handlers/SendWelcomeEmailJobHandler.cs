using System.Text.Json;

namespace JobSchedulerPrototype.Jobs;

public sealed class SendWelcomeEmailJobHandler : IJobHandler
{
    public string Type => JobTypes.SendWelcomeEmail;

    public Task<JobExecutionResult> ExecuteAsync(JobRecord job, CancellationToken cancellationToken)
    {
        SendWelcomeEmailJobPayload? payload;
        try
        {
            payload = job.Payload.Deserialize<SendWelcomeEmailJobPayload>();
        }
        catch (JsonException)
        {
            return Task.FromResult(JobExecutionResult.Failure("Stored job payload is invalid."));
        }

        if (payload is null)
        {
            return Task.FromResult(JobExecutionResult.Failure("Stored job payload is invalid."));
        }

        if (payload.ShouldFail)
        {
            return Task.FromResult(JobExecutionResult.Failure("Simulated welcome email failure."));
        }

        return Task.FromResult(JobExecutionResult.Success());
    }
}
