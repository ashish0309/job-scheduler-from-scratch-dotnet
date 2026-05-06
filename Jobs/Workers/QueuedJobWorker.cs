using Microsoft.Extensions.Options;

namespace JobSchedulerPrototype.Jobs;

public sealed class QueuedJobWorker : IJobWorker
{
    private readonly IJobLifecycleService _lifecycle;
    private readonly IJobDispatcher _dispatcher;
    private readonly IDataAccessScopeProvider _dataAccessScopeProvider;
    private readonly ILogger<QueuedJobWorker> _logger;
    private readonly TimeSpan _simulatedWorkDuration;
    private readonly TimeSpan _leaseDuration;
    private readonly TimeSpan _leaseRenewalInterval;

    public QueuedJobWorker(
        IJobLifecycleService lifecycle,
        IJobDispatcher dispatcher,
        ILogger<QueuedJobWorker> logger,
        IOptions<JobWorkerOptions> options,
        IDataAccessScopeProvider dataAccessScopeProvider)
        : this(
            lifecycle,
            dispatcher,
            logger,
            options.Value.ValidSimulatedWorkDuration,
            options.Value.ValidLeaseDuration,
            dataAccessScopeProvider)
    {
    }

    public QueuedJobWorker(
        IJobLifecycleService lifecycle,
        IJobDispatcher dispatcher,
        ILogger<QueuedJobWorker> logger,
        IOptions<JobWorkerOptions> options)
        : this(
            lifecycle,
            dispatcher,
            logger,
            options,
            new FixedDataAccessScopeProvider(DataAccessScope.AllTenants()))
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
        : this(
            lifecycle,
            dispatcher,
            logger,
            simulatedWorkDuration,
            leaseDuration,
            new FixedDataAccessScopeProvider(DataAccessScope.AllTenants()))
    {
    }

    public QueuedJobWorker(
        IJobLifecycleService lifecycle,
        IJobDispatcher dispatcher,
        ILogger<QueuedJobWorker> logger,
        TimeSpan simulatedWorkDuration,
        TimeSpan leaseDuration,
        IDataAccessScopeProvider dataAccessScopeProvider)
    {
        _lifecycle = lifecycle;
        _dispatcher = dispatcher;
        _dataAccessScopeProvider = dataAccessScopeProvider;
        _logger = logger;
        _simulatedWorkDuration = simulatedWorkDuration;
        _leaseDuration = leaseDuration;
        _leaseRenewalInterval = LeaseRenewalIntervalFor(leaseDuration);
    }

    public async Task<bool> ProcessNextJobAsync(string workerId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        JobRecord? job;
        using (_dataAccessScopeProvider.BeginScope(DataAccessScope.AllTenants()))
        {
            job = _lifecycle.ClaimNextDueJob(now, workerId, now.Add(_leaseDuration));
        }

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

        using var leaseRenewalCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var leaseRenewalTask = RenewLeaseUntilExecutionStops(
            job,
            workerId,
            leaseRenewalCancellation.Token);
        JobExecutionResult result;

        try
        {
            await Task.Delay(_simulatedWorkDuration, cancellationToken);
            using (_dataAccessScopeProvider.BeginScope(DataAccessScope.Tenant(job.TenantId)))
            {
                result = await ExecuteJob(job, cancellationToken);
            }
        }
        finally
        {
            await StopLeaseRenewal(leaseRenewalCancellation, leaseRenewalTask);
        }

        JobExecutionCompletion completion;
        using (_dataAccessScopeProvider.BeginScope(DataAccessScope.AllTenants()))
        {
            completion = _lifecycle.CompleteExecution(job, result);
        }

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
        else if (completion.Status == JobExecutionCompletionStatus.LeaseLost)
        {
            _logger.LogWarning(
                "Worker {WorkerId} finished job {JobId} type={JobType} attempt={AttemptNumber}, but no longer owns the running lease",
                workerId,
                job.Id,
                job.Type,
                job.AttemptCount);
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

    private async Task RenewLeaseUntilExecutionStops(
        JobRecord job,
        string workerId,
        CancellationToken cancellationToken)
    {
        using var scope = _dataAccessScopeProvider.BeginScope(DataAccessScope.AllTenants());

        try
        {
            while (true)
            {
                await Task.Delay(_leaseRenewalInterval, cancellationToken);

                var renewedAt = DateTimeOffset.UtcNow;
                var renewed = _lifecycle.RenewLease(
                    job,
                    renewedAt,
                    renewedAt.Add(_leaseDuration));

                if (!renewed)
                {
                    _logger.LogWarning(
                        "Worker {WorkerId} could not renew lease for job {JobId} type={JobType} attempt={AttemptNumber}",
                        workerId,
                        job.Id,
                        job.Type,
                        job.AttemptCount);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Worker {WorkerId} lease renewal failed for job {JobId} type={JobType} attempt={AttemptNumber}",
                workerId,
                job.Id,
                job.Type,
                job.AttemptCount);
        }
    }

    private static async Task StopLeaseRenewal(
        CancellationTokenSource leaseRenewalCancellation,
        Task leaseRenewalTask)
    {
        await leaseRenewalCancellation.CancelAsync();
        await leaseRenewalTask;
    }

    private static TimeSpan LeaseRenewalIntervalFor(TimeSpan leaseDuration)
    {
        return TimeSpan.FromTicks(Math.Max(TimeSpan.FromMilliseconds(1).Ticks, leaseDuration.Ticks / 2));
    }
}
