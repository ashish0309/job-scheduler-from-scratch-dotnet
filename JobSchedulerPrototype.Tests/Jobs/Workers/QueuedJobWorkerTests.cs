using System.Text.Json;
using JobSchedulerPrototype.Jobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class QueuedJobWorkerTests
{
    [Fact]
    public async Task ProcessNextJobAsyncCompletesJobWhenExecutionSucceeds()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        store.Add(job);
        var actions = ActionDispatcher(store);
        var worker = new QueuedJobWorker(
            actions,
            new StubJobDispatcher(JobExecutionResult.Success()),
            NullLogger<QueuedJobWorker>.Instance,
            simulatedWorkDuration: TimeSpan.Zero);

        var processedJob = await worker.ProcessNextJobAsync("worker-1", CancellationToken.None);

        Assert.True(processedJob);
        var completedJob = store.Get(job.Id);
        Assert.NotNull(completedJob);
        Assert.Equal(JobStatus.Completed, completedJob.Status);
        Assert.NotNull(completedJob.StartedAt);
        Assert.NotNull(completedJob.CompletedAt);
        Assert.Null(completedJob.FailedAt);
        Assert.Contains(
            actions.DispatchedRequestTypes,
            static requestType => requestType == typeof(ClaimNextDueJobActionRequest));
        Assert.Contains(
            actions.DispatchedRequestTypes,
            static requestType => requestType == typeof(CompleteJobExecutionActionRequest));
    }

    [Fact]
    public async Task ProcessNextJobAsyncExecutesHandlerUnderJobTenantScope()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        var dataAccessScopeProvider = new FixedDataAccessScopeProvider(DataAccessScope.AllTenants());
        var dispatcher = new CapturingDataAccessScopeJobDispatcher(dataAccessScopeProvider);
        var actions = ActionDispatcher(store);
        store.Add(job);
        var worker = new QueuedJobWorker(
            actions,
            dispatcher,
            NullLogger<QueuedJobWorker>.Instance,
            simulatedWorkDuration: TimeSpan.Zero,
            leaseDuration: TimeSpan.FromSeconds(60),
            dataAccessScopeProvider);

        var processedJob = await worker.ProcessNextJobAsync("worker-1", CancellationToken.None);

        Assert.True(processedJob);
        Assert.NotNull(dispatcher.CapturedScope);
        Assert.False(dispatcher.CapturedScope.IncludesAllTenants);
        Assert.Equal(job.TenantId, dispatcher.CapturedScope.TenantId);
    }

    [Fact]
    public async Task ProcessNextJobAsyncRenewsLeaseWhileExecutionIsRunning()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        var dispatcher = new BlockingJobDispatcher();
        var actions = ActionDispatcher(store);
        var leaseDuration = TimeSpan.FromMilliseconds(120);
        store.Add(job);
        var worker = new QueuedJobWorker(
            actions,
            dispatcher,
            NullLogger<QueuedJobWorker>.Instance,
            simulatedWorkDuration: TimeSpan.Zero,
            leaseDuration);

        var processingTask = worker.ProcessNextJobAsync("worker-1", CancellationToken.None);
        await dispatcher.ExecutionStarted.WaitAsync(TimeSpan.FromSeconds(5));
        var initialLeaseExpiresAt = store.Get(job.Id)?.LeaseExpiresAt;
        Assert.NotNull(initialLeaseExpiresAt);

        await WaitUntilAsync(
            () => store.Get(job.Id)?.LeaseExpiresAt > initialLeaseExpiresAt,
            TimeSpan.FromSeconds(5));
        var reclaimAttempt = store.TryClaimNextDueJob(
            initialLeaseExpiresAt.Value,
            "worker-2",
            initialLeaseExpiresAt.Value.Add(leaseDuration));

        Assert.Null(reclaimAttempt);
        dispatcher.Complete(JobExecutionResult.Success());
        Assert.True(await processingTask.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(JobStatus.Completed, store.Get(job.Id)?.Status);
        Assert.True(actions.RenewLeaseDispatchCount > 0);
    }

    [Fact]
    public async Task ProcessNextJobAsyncSchedulesRetryWhenExecutionFailsAndAttemptsRemain()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        var actions = ActionDispatcher(store);
        store.Add(job);
        var worker = new QueuedJobWorker(
            actions,
            new StubJobDispatcher(JobExecutionResult.Failure("Simulated welcome email failure.")),
            NullLogger<QueuedJobWorker>.Instance,
            simulatedWorkDuration: TimeSpan.Zero);

        var processedJob = await worker.ProcessNextJobAsync("worker-1", CancellationToken.None);

        Assert.True(processedJob);
        var retriedJob = store.Get(job.Id);
        Assert.NotNull(retriedJob);
        Assert.Equal(JobStatus.Scheduled, retriedJob.Status);
        Assert.Equal("Simulated welcome email failure.", retriedJob.FailureReason);
        Assert.NotNull(retriedJob.StartedAt);
        Assert.Null(retriedJob.CompletedAt);
        Assert.NotNull(retriedJob.FailedAt);
        Assert.NotNull(retriedJob.ScheduledAt);
        Assert.Equal(
            [JobStatus.Queued, JobStatus.Running, JobStatus.Failed, JobStatus.Scheduled],
            retriedJob.History.Select(change => change.Status));
        Assert.Equal("Retry scheduled.", retriedJob.History[^1].Reason);
    }

    [Fact]
    public async Task ProcessNextJobAsyncFailsJobWhenExecutionFailsAndAttemptsAreExhausted()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob(maxAttempts: 1);
        var actions = ActionDispatcher(store);
        store.Add(job);
        var worker = new QueuedJobWorker(
            actions,
            new StubJobDispatcher(JobExecutionResult.Failure("Simulated welcome email failure.")),
            NullLogger<QueuedJobWorker>.Instance,
            simulatedWorkDuration: TimeSpan.Zero);

        var processedJob = await worker.ProcessNextJobAsync("worker-1", CancellationToken.None);

        Assert.True(processedJob);
        var failedJob = store.Get(job.Id);
        Assert.NotNull(failedJob);
        Assert.Equal(JobStatus.Failed, failedJob.Status);
        Assert.Equal("Simulated welcome email failure.", failedJob.FailureReason);
        Assert.NotNull(failedJob.StartedAt);
        Assert.Null(failedJob.CompletedAt);
        Assert.NotNull(failedJob.FailedAt);
    }

    [Fact]
    public async Task ProcessNextJobAsyncSchedulesRetryWhenExecutionThrowsAndAttemptsRemain()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob();
        var actions = ActionDispatcher(store);
        store.Add(job);
        var worker = new QueuedJobWorker(
            actions,
            new ThrowingJobDispatcher(),
            NullLogger<QueuedJobWorker>.Instance,
            simulatedWorkDuration: TimeSpan.Zero);

        var processedJob = await worker.ProcessNextJobAsync("worker-1", CancellationToken.None);

        Assert.True(processedJob);
        var retriedJob = store.Get(job.Id);
        Assert.NotNull(retriedJob);
        Assert.Equal(JobStatus.Scheduled, retriedJob.Status);
        Assert.Equal("Job execution threw an unhandled exception.", retriedJob.FailureReason);
    }

    [Fact]
    public async Task ProcessNextJobAsyncFailsJobWhenExecutionThrowsAndAttemptsAreExhausted()
    {
        var store = new InMemoryJobStore();
        var job = CreateJob(maxAttempts: 1);
        var actions = ActionDispatcher(store);
        store.Add(job);
        var worker = new QueuedJobWorker(
            actions,
            new ThrowingJobDispatcher(),
            NullLogger<QueuedJobWorker>.Instance,
            simulatedWorkDuration: TimeSpan.Zero);

        var processedJob = await worker.ProcessNextJobAsync("worker-1", CancellationToken.None);

        Assert.True(processedJob);
        var failedJob = store.Get(job.Id);
        Assert.NotNull(failedJob);
        Assert.Equal(JobStatus.Failed, failedJob.Status);
        Assert.Equal("Job execution threw an unhandled exception.", failedJob.FailureReason);
    }

    [Fact]
    public async Task ProcessNextJobAsyncReturnsFalseWhenNoQueuedJobExists()
    {
        var store = new InMemoryJobStore();
        var actions = ActionDispatcher(store);
        var worker = new QueuedJobWorker(
            actions,
            new StubJobDispatcher(JobExecutionResult.Success()),
            NullLogger<QueuedJobWorker>.Instance,
            simulatedWorkDuration: TimeSpan.Zero);

        var processedJob = await worker.ProcessNextJobAsync("worker-1", CancellationToken.None);

        Assert.False(processedJob);
    }

    private static JobRecord CreateJob(int maxAttempts = 3)
    {
        return JobRecord.Enqueue(
            Guid.NewGuid(),
            TestJobActorProvider.TenantId,
            TestJobActorProvider.ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts,
            new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));
    }

    private static IJobDefinitionRegistry DefinitionRegistry()
    {
        return new JobDefinitionRegistry([new SendWelcomeEmailJobDefinition()]);
    }

    private static IJobLifecycleService LifecycleService(IJobStore store)
    {
        return new JobLifecycleService(
            store,
            DefinitionRegistry(),
            new TestJobActorProvider());
    }

    private static LifecycleBackedJobActionDispatcher ActionDispatcher(IJobStore store)
    {
        return new LifecycleBackedJobActionDispatcher(LifecycleService(store));
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse("""{"userId":"user_123","email":"person@example.com"}""");
        return document.RootElement.Clone();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var timeoutCancellation = new CancellationTokenSource(timeout);

        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeoutCancellation.Token);
        }
    }

    private sealed class StubJobDispatcher : IJobDispatcher
    {
        private readonly JobExecutionResult _result;

        public StubJobDispatcher(JobExecutionResult result)
        {
            _result = result;
        }

        public Task<JobExecutionResult> ExecuteAsync(JobRecord job, CancellationToken cancellationToken)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class CapturingDataAccessScopeJobDispatcher : IJobDispatcher
    {
        private readonly IDataAccessScopeProvider _dataAccessScopeProvider;

        public CapturingDataAccessScopeJobDispatcher(IDataAccessScopeProvider dataAccessScopeProvider)
        {
            _dataAccessScopeProvider = dataAccessScopeProvider;
        }

        public DataAccessScope? CapturedScope { get; private set; }

        public Task<JobExecutionResult> ExecuteAsync(JobRecord job, CancellationToken cancellationToken)
        {
            CapturedScope = _dataAccessScopeProvider.Current;
            return Task.FromResult(JobExecutionResult.Success());
        }
    }

    private sealed class ThrowingJobDispatcher : IJobDispatcher
    {
        public Task<JobExecutionResult> ExecuteAsync(JobRecord job, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The job handler exploded.");
        }
    }

    private sealed class BlockingJobDispatcher : IJobDispatcher
    {
        private readonly TaskCompletionSource<JobExecutionResult> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _executionStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ExecutionStarted => _executionStarted.Task;

        public void Complete(JobExecutionResult result)
        {
            _completion.SetResult(result);
        }

        public Task<JobExecutionResult> ExecuteAsync(JobRecord job, CancellationToken cancellationToken)
        {
            _executionStarted.SetResult();
            return _completion.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class LifecycleBackedJobActionDispatcher : IJobActionDispatcher
    {
        private readonly IJobLifecycleService _lifecycle;

        public LifecycleBackedJobActionDispatcher(IJobLifecycleService lifecycle)
        {
            _lifecycle = lifecycle;
        }

        public List<Type> DispatchedRequestTypes { get; } = [];

        public int RenewLeaseDispatchCount { get; private set; }

        public Task<TResult> DispatchAsync<TResult>(
            IJobActionRequest<TResult> request,
            CancellationToken cancellationToken = default)
        {
            DispatchedRequestTypes.Add(request.GetType());

            object result = request switch
            {
                ClaimNextDueJobActionRequest claimRequest => Claim(claimRequest),
                RenewJobLeaseActionRequest renewRequest => Renew(renewRequest),
                CompleteJobExecutionActionRequest completeRequest => Complete(completeRequest),
                _ => throw new InvalidOperationException(
                    $"Unsupported test action request type '{request.GetType().Name}'.")
            };

            return Task.FromResult((TResult)result);
        }

        private ClaimNextDueJobActionResult Claim(ClaimNextDueJobActionRequest request)
        {
            var claimedJob = _lifecycle.ClaimNextDueJob(
                request.Now,
                request.WorkerId,
                request.LeaseExpiresAt);
            return ClaimNextDueJobActionResult.Authorized(claimedJob);
        }

        private RenewJobLeaseActionResult Renew(RenewJobLeaseActionRequest request)
        {
            RenewLeaseDispatchCount++;
            var renewed = _lifecycle.RenewLease(
                request.Job,
                request.RenewedAt,
                request.LeaseExpiresAt);
            return RenewJobLeaseActionResult.Authorized(renewed);
        }

        private CompleteJobExecutionActionResult Complete(CompleteJobExecutionActionRequest request)
        {
            var completion = _lifecycle.CompleteExecution(request.Job, request.Result);
            return CompleteJobExecutionActionResult.Authorized(completion);
        }
    }
}
