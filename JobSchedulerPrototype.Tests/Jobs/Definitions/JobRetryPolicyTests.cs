using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class JobRetryPolicyTests
{
    [Fact]
    public void CreateReturnsRetryPolicyWhenValuesAreValid()
    {
        var policy = JobRetryPolicy.Create(
            maxAttempts: 3,
            delay: TimeSpan.FromSeconds(10));

        Assert.Equal(3, policy.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(10), policy.Delay);
    }

    [Fact]
    public void CreateRejectsMaxAttemptsLessThanOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobRetryPolicy.Create(
            maxAttempts: 0,
            delay: TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void CreateRejectsNegativeDelay()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobRetryPolicy.Create(
            maxAttempts: 3,
            delay: TimeSpan.FromSeconds(-1)));
    }
}
