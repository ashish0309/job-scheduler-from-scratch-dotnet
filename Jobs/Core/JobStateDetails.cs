namespace JobSchedulerPrototype.Jobs;

public abstract record JobStateDetails
{
    public static JobStateDetails None { get; } = new EmptyJobStateDetails();

    private sealed record EmptyJobStateDetails : JobStateDetails;
}

public sealed record ScheduledJobStateDetails(DateTimeOffset ScheduledAt) : JobStateDetails;
