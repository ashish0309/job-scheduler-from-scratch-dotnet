namespace JobSchedulerPrototype.Jobs;

public sealed class QueuedJobWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SimulatedWorkDuration = TimeSpan.FromSeconds(2);

    private readonly IJobStore _jobs;
    private readonly IJobDispatcher _dispatcher;
    private readonly IJobDefinitionRegistry _definitions;
    private readonly ILogger<QueuedJobWorker> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _simulatedWorkDuration;

    public QueuedJobWorker(
        IJobStore jobs,
        IJobDispatcher dispatcher,
        IJobDefinitionRegistry definitions,
        ILogger<QueuedJobWorker> logger)
        : this(jobs, dispatcher, definitions, logger, PollInterval, SimulatedWorkDuration)
    {
    }

    public QueuedJobWorker(
        IJobStore jobs,
        IJobDispatcher dispatcher,
        IJobDefinitionRegistry definitions,
        ILogger<QueuedJobWorker> logger,
        TimeSpan pollInterval,
        TimeSpan simulatedWorkDuration)
    {
        _jobs = jobs;
        _dispatcher = dispatcher;
        _definitions = definitions;
        _logger = logger;
        _pollInterval = pollInterval;
        _simulatedWorkDuration = simulatedWorkDuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processedJob = await ProcessNextJobAsync(stoppingToken);
            if (!processedJob)
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
        }
    }

    public async Task<bool> ProcessNextJobAsync(CancellationToken cancellationToken)
    {
        var job = _jobs.TryClaimNextDueJob(DateTimeOffset.UtcNow);
        if (job is null)
        {
            return false;
        }

        _logger.LogInformation(
            "Claimed job {JobId} type={JobType} attempt={AttemptNumber}",
            job.Id,
            job.Type,
            job.AttemptCount);

        await Task.Delay(_simulatedWorkDuration, cancellationToken);
        var result = await _dispatcher.ExecuteAsync(job, cancellationToken);
        var failureReason = result.FailureReason ?? "Job execution failed.";

        if (result.Succeeded)
        {
            _jobs.MarkCompleted(job.Id);
            _logger.LogInformation(
                "Completed job {JobId} type={JobType} attempt={AttemptNumber}",
                job.Id,
                job.Type,
                job.AttemptCount);
        }
        else if (job.AttemptCount < job.MaxAttempts
            && _definitions.Find(job.Type) is { } definition)
        {
            var scheduledAt = DateTimeOffset.UtcNow.AddSeconds(definition.RetryDelaySeconds);
            _jobs.ScheduleRetry(
                job.Id,
                failureReason,
                scheduledAt);
            _logger.LogWarning(
                "Failed job {JobId} type={JobType} attempt={AttemptNumber} reason={FailureReason}; scheduled retry attempt={NextAttemptNumber} runAt={ScheduledAt}",
                job.Id,
                job.Type,
                job.AttemptCount,
                failureReason,
                job.AttemptCount + 1,
                scheduledAt);
        }
        else
        {
            _jobs.MarkFailed(job.Id, failureReason);
            _logger.LogError(
                "Failed job {JobId} type={JobType} finalAttempt={AttemptNumber} reason={FailureReason}",
                job.Id,
                job.Type,
                job.AttemptCount,
                failureReason);
        }

        return true;
    }
}
