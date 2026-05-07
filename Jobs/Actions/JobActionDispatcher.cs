using Microsoft.Extensions.DependencyInjection;

namespace JobSchedulerPrototype.Jobs;

public sealed class JobActionDispatcher : IJobActionDispatcher
{
    private readonly IServiceProvider _services;

    public JobActionDispatcher(IServiceProvider services)
    {
        _services = services;
    }

    public Task<TResult> DispatchAsync<TResult>(
        IJobActionRequest<TResult> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var handlerType = typeof(IJobActionHandler<,>).MakeGenericType(requestType, typeof(TResult));
        var handler = _services.GetRequiredService(handlerType);
        var execute = handlerType.GetMethod("ExecuteAsync")
            ?? throw new InvalidOperationException(
                $"No ExecuteAsync method found on action handler type '{handlerType}'.");

        var execution = execute.Invoke(handler, new object?[] { request, cancellationToken })
            ?? throw new InvalidOperationException(
                $"Action handler '{handlerType}' returned null execution task.");

        return (Task<TResult>)execution;
    }
}
