using JobSchedulerPrototype.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class JobActionDispatcherTests
{
    [Fact]
    public async Task DispatchAsyncResolvesAndExecutesMatchingHandler()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IJobActionDispatcher, JobActionDispatcher>();
        services.AddSingleton<IJobActionHandler<TestActionRequest, string>, TestActionHandler>();
        using var serviceProvider = services.BuildServiceProvider();

        var dispatcher = serviceProvider.GetRequiredService<IJobActionDispatcher>();
        var response = await dispatcher.DispatchAsync(
            new TestActionRequest("hello"));

        Assert.Equal("echo:hello", response);
    }

    private sealed record TestActionRequest(string Value) : IJobActionRequest<string>;

    private sealed class TestActionHandler : IJobActionHandler<TestActionRequest, string>
    {
        public Task<string> ExecuteAsync(
            TestActionRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult($"echo:{request.Value}");
        }
    }
}
