using Microsoft.Extensions.Options;

namespace JobSchedulerPrototype.Jobs;

public sealed class QueuedJobWorker : IJobWorker
{
    private readonly IJobLifecycleService _lifecycle;
    private readonly IJobDispatcher _dispatcher;
    private readonly ILogger<QueuedJobWorker> _logger;
    private readonly TimeSpan _simulatedWorkDuration;
    private readonly TimeSpan _leaseDuration;

    public QueuedJobWorker(
        IJobLifecycleService lifecycle,
        IJobDispatcher dispatcher,
        ILogger<QueuedJobWorker> logger,
        IOptions<JobWorkerOptions> options)
        : this(
            lifecycle,
            dispatcher,
            logger,
            options.Value.ValidSimulatedWorkDuration,
            options.Value.ValidLeaseDuration)
    {
    }

    public QueuedJobWorker(
        IJobLifecycleService lifecycle,
        IJobDispatcher dispatcher,
        ILogger<QueuedJobWorker> logger,
        TimeSpan simulatedWorkDuration)
        : this(lifecycle, dispatcher, logger, simulatedWorkDuration, TimeSpan.FromSeconds(60))
    {
    }

    public QueuedJobWorker(
        IJobLifecycleService lifecycle,
        IJobDispatcher dispatcher,
        ILogger<QueuedJobWorker> logger,
        TimeSpan simulatedWorkDuration,
        TimeSpan leaseDuration)
    {
        _lifecycle = lifecycle;
        _dispatcher = dispatcher;
        _logger = logger;
        _simulatedWorkDuration = simulatedWorkDuration;
        _leaseDuration = leaseDuration;
    }

    public async Task<bool> ProcessNextJobAsync(string workerId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var job = _lifecycle.ClaimNextDueJob(now, workerId, now.Add(_leaseDuration));
        if (job is null)
        {
            return false;
        }

        _logger.LogInformation(
            "Worker {WorkerId} claimed job {JobId} type={JobType} attempt={AttemptNumber}",
            workerId,
            job.Id,
            job.Type,
            job.AttemptCount);

        await Task.Delay(_simulatedWorkDuration, cancellationToken);
        var result = await ExecuteJob(job, cancellationToken);
        var completion = _lifecycle.CompleteExecution(job, result);

        if (completion.Status == JobExecutionCompletionStatus.Completed)
        {
            _logger.LogInformation(
                "Worker {WorkerId} completed job {JobId} type={JobType} attempt={AttemptNumber}",
                workerId,
                job.Id,
                job.Type,
                job.AttemptCount);
        }
        else if (completion.Status == JobExecutionCompletionStatus.RetryScheduled)
        {
            _logger.LogWarning(
                "Worker {WorkerId} failed job {JobId} type={JobType} attempt={AttemptNumber} reason={FailureReason}; scheduled retry attempt={NextAttemptNumber} runAt={ScheduledAt}",
                workerId,
                job.Id,
                job.Type,
                job.AttemptCount,
                completion.FailureReason,
                job.AttemptCount + 1,
                completion.RetryScheduledAt);
        }
        else
        {
            _logger.LogError(
                "Worker {WorkerId} failed job {JobId} type={JobType} finalAttempt={AttemptNumber} reason={FailureReason}",
                workerId,
                job.Id,
                job.Type,
                job.AttemptCount,
                completion.FailureReason);
        }

        return true;
    }

    private async Task<JobExecutionResult> ExecuteJob(
        JobRecord job,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _dispatcher.ExecuteAsync(job, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Job {JobId} type={JobType} attempt={AttemptNumber} threw during execution",
                job.Id,
                job.Type,
                job.AttemptCount);

            return JobExecutionResult.Failure("Job execution threw an unhandled exception.");
        }
    }
}
