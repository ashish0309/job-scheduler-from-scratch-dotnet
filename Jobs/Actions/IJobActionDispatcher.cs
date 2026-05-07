namespace JobSchedulerPrototype.Jobs;

public interface IJobActionDispatcher
{
    Task<TResult> DispatchAsync<TResult>(
        IJobActionRequest<TResult> request,
        CancellationToken cancellationToken = default);
}
