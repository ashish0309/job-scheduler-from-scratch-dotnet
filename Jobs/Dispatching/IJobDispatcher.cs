namespace JobSchedulerPrototype.Jobs;

public interface IJobDispatcher
{
    Task<JobExecutionResult> ExecuteAsync(JobRecord job, CancellationToken cancellationToken);
}
