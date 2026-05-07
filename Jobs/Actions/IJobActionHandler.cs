namespace JobSchedulerPrototype.Jobs;

public interface IJobActionHandler<in TRequest, TResult>
    where TRequest : IJobActionRequest<TResult>
{
    Task<TResult> ExecuteAsync(
        TRequest request,
        CancellationToken cancellationToken = default);
}
