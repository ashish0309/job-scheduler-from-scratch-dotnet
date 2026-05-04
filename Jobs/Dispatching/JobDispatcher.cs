namespace JobSchedulerPrototype.Jobs;

public sealed class JobDispatcher : IJobDispatcher
{
    private readonly IJobHandlerRegistry _handlers;

    public JobDispatcher(IJobHandlerRegistry handlers)
    {
        _handlers = handlers;
    }

    public Task<JobExecutionResult> ExecuteAsync(JobRecord job, CancellationToken cancellationToken)
    {
        var handler = _handlers.Find(job.Type);
        if (handler is null)
        {
            return Task.FromResult(JobExecutionResult.Failure("No executor is registered for this job type."));
        }

        return handler.ExecuteAsync(job, cancellationToken);
    }
}
