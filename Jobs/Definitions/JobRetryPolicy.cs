namespace JobSchedulerPrototype.Jobs;

public sealed record JobRetryPolicy(int MaxAttempts, TimeSpan Delay)
{
    public static JobRetryPolicy Create(int maxAttempts, TimeSpan delay)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Max attempts must be at least 1.");
        }

        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), "Retry delay cannot be negative.");
        }

        return new JobRetryPolicy(maxAttempts, delay);
    }
}
