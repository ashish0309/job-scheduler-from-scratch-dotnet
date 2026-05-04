namespace JobSchedulerPrototype.Jobs;

public interface IJobHandler
{
    string Type { get; }

    Task<JobExecutionResult> ExecuteAsync(JobRecord job, CancellationToken cancellationToken);
}
