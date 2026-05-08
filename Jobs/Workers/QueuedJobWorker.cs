using Microsoft.Extensions.Options;

namespace JobSchedulerPrototype.Jobs;

public sealed class QueuedJobWorker : IJobWorker
{
    private readonly IJobActionDispatcher _actions;
    private readonly IJobDispatcher _dispatcher;
    private readonly IDataAccessScopeProvider _dataAccessScopeProvider;
    private readonly ILogger<QueuedJobWorker> _logger;
    private readonly TimeSpan _simulatedWorkDuration;
    private readonly TimeSpan _leaseDuration;
    private readonly TimeSpan _leaseRenewalInterval;
    private readonly JobActor _workerActor;

    public QueuedJobWorker(
        IJobActionDispatcher actions,
        IJobDispatcher dispatcher,
        ILogger<QueuedJobWorker> logger,
        IOptions<JobWorkerOptions> options,
        IOptions<WorkerActorOptions> workerActorOptions,
        IDataAccessScopeProvider dataAccessScopeProvider)
        : this(
            actions,
            dispatcher,
            logger,
            options.Value.ValidSimulatedWorkDuration,
            options.Value.ValidLeaseDuration,
            dataAccessScopeProvider,
            workerActorOptions.Value.ToActor())
    {
    }

    public QueuedJobWorker(
        IJobActionDispatcher actions,
        IJobDispatcher dispatcher,
        ILogger<QueuedJobWorker> logger,
        IOptions<JobWorkerOptions> options)
        : this(
            actions,
            dispatcher,
            logger,
            options,
            Microsoft.Extensions.Options.Options.Create(new WorkerActorOptions()),
            new FixedDataAccessScopeProvider(DataAccessScope.AllTenants()))
    {
    }

    public QueuedJobWorker(
        IJobActionDispatcher actions,
        IJobDispatcher dispatcher,
        ILogger<QueuedJobWorker> logger,
        TimeSpan simulatedWorkDuration)
        : this(actions, dispatcher, logger, simulatedWorkDuration, TimeSpan.FromSeconds(60))
    {
    }

    public QueuedJobWorker(
        IJobActionDispatcher actions,
        IJobDispatcher dispatcher,
        ILogger<QueuedJobWorker> logger,
        TimeSpan simulatedWorkDuration,
        TimeSpan leaseDuration)
        : this(
            actions,
            dispatcher,
            logger,
            simulatedWorkDuration,
            leaseDuration,
            new FixedDataAccessScopeProvider(DataAccessScope.AllTenants()),
            new WorkerActorOptions().ToActor())
    {
    }

    public QueuedJobWorker(
        IJobActionDispatcher actions,
        IJobDispatcher dispatcher,
        ILogger<QueuedJobWorker> logger,
        TimeSpan simulatedWorkDuration,
        TimeSpan leaseDuration,
        IDataAccessScopeProvider dataAccessScopeProvider,
        JobActor? workerActor = null)
    {
        _actions = actions;
        _dispatcher = dispatcher;
        _dataAccessScopeProvider = dataAccessScopeProvider;
        _logger = logger;
        _simulatedWorkDuration = simulatedWorkDuration;
        _leaseDuration = leaseDuration;
        _leaseRenewalInterval = LeaseRenewalIntervalFor(leaseDuration);
        _workerActor = workerActor ?? new WorkerActorOptions().ToActor();
    }

    public async Task<bool> ProcessNextJobAsync(string workerId, CancellationToken cancellationToken)
    {
        using var actorScope = _dataAccessScopeProvider.BeginActorScope(_workerActor);

        var now = DateTimeOffset.UtcNow;
        var claimResult = await _actions.DispatchAsync(
            new ClaimNextDueJobActionRequest(
                now,
                workerId,
                now.Add(_leaseDuration)),
            cancellationToken);

        if (!claimResult.IsAuthorized)
        {
            _logger.LogError(
                "Worker {WorkerId} was not authorized to claim jobs: {ErrorMessage}",
                workerId,
                claimResult.ErrorMessage);
            return false;
        }

        var job = claimResult.Job;
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

        var completionResult = await _actions.DispatchAsync(
            new CompleteJobExecutionActionRequest(job, result),
            cancellationToken);

        if (!completionResult.IsAuthorized)
        {
            _logger.LogError(
                "Worker {WorkerId} was not authorized to complete job {JobId}: {ErrorMessage}",
                workerId,
                job.Id,
                completionResult.ErrorMessage);
            return true;
        }

        var completion = completionResult.Completion
            ?? throw new InvalidOperationException(
                $"Completion action did not provide a completion result for job '{job.Id}'.");

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
        try
        {
            while (true)
            {
                await Task.Delay(_leaseRenewalInterval, cancellationToken);

                var renewedAt = DateTimeOffset.UtcNow;
                var renewalResult = await _actions.DispatchAsync(
                    new RenewJobLeaseActionRequest(
                        job,
                        renewedAt,
                        renewedAt.Add(_leaseDuration)),
                    cancellationToken);

                if (!renewalResult.IsAuthorized)
                {
                    _logger.LogError(
                        "Worker {WorkerId} was not authorized to renew lease for job {JobId} type={JobType} attempt={AttemptNumber}: {ErrorMessage}",
                        workerId,
                        job.Id,
                        job.Type,
                        job.AttemptCount,
                        renewalResult.ErrorMessage);
                    return;
                }

                if (!renewalResult.Renewed)
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
