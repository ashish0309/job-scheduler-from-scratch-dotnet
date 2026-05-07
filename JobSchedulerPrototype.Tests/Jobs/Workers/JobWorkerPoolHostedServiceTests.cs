using System.Text.Json;
using JobSchedulerPrototype.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class JobWorkerPoolHostedServiceTests
{
    [Fact]
    public async Task WorkerPoolProcessesJobsConcurrentlyWhenMultipleWorkersAreConfigured()
    {
        var store = new InMemoryJobStore();
        var dispatcher = new TrackingJobDispatcher(expectedExecutions: 6);
        var options = Options.Create(new JobWorkerOptions
        {
            WorkerCount = 3,
            PollIntervalSeconds = 1,
            SimulatedWorkDurationSeconds = 0
        });
        var actions = ActionDispatcher(store);
        var worker = new QueuedJobWorker(
            actions,
            dispatcher,
            NullLogger<QueuedJobWorker>.Instance,
            simulatedWorkDuration: TimeSpan.Zero);
        var pool = new JobWorkerPoolHostedService(
            worker,
            options,
            NullLogger<JobWorkerPoolHostedService>.Instance);

        for (var index = 0; index < 6; index++)
        {
            store.Add(CreateJob(index));
        }

        await pool.StartAsync(CancellationToken.None);

        await dispatcher.AllExecutionsCompleted.WaitAsync(TimeSpan.FromSeconds(5));
        await StopPoolAsync(pool);

        Assert.Equal(6, dispatcher.ExecutionCount);
        Assert.True(dispatcher.MaxConcurrentExecutions > 1);
        Assert.All(store.List(), job => Assert.Equal(JobStatus.Completed, job.Status));
    }

    [Fact]
    public async Task WorkerPoolContinuesAfterWorkerThrows()
    {
        var worker = new FaultThenSuccessWorker();
        var options = Options.Create(new JobWorkerOptions
        {
            WorkerCount = 1,
            PollIntervalSeconds = 1,
            SimulatedWorkDurationSeconds = 0
        });
        var pool = new JobWorkerPoolHostedService(
            worker,
            options,
            NullLogger<JobWorkerPoolHostedService>.Instance);

        await pool.StartAsync(CancellationToken.None);

        await worker.SuccessfulExecutionCompleted.WaitAsync(TimeSpan.FromSeconds(5));
        await StopPoolAsync(pool);

        Assert.Equal(2, worker.CallCount);
    }

    private static JobRecord CreateJob(int index)
    {
        return JobRecord.Enqueue(
            Guid.NewGuid(),
            TestJobActorProvider.TenantId,
            TestJobActorProvider.ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 1,
            new DateTimeOffset(2026, 5, 4, 10, 0, index, TimeSpan.Zero));
    }

    private static IJobLifecycleService LifecycleService(IJobStore store)
    {
        return new JobLifecycleService(
            store,
            new JobDefinitionRegistry([new SendWelcomeEmailJobDefinition()]),
            new TestJobActorProvider());
    }

    private static IJobActionDispatcher ActionDispatcher(IJobStore store)
    {
        return new LifecycleBackedJobActionDispatcher(LifecycleService(store));
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse("""{"userId":"user_123","email":"person@example.com"}""");
        return document.RootElement.Clone();
    }

    private static async Task StopPoolAsync(JobWorkerPoolHostedService pool)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await pool.StopAsync(timeout.Token);
    }

    private sealed class TrackingJobDispatcher : IJobDispatcher
    {
        private readonly int _expectedExecutions;
        private readonly TaskCompletionSource _allExecutionsCompleted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeExecutions;
        private int _executionCount;
        private int _maxConcurrentExecutions;

        public TrackingJobDispatcher(int expectedExecutions)
        {
            _expectedExecutions = expectedExecutions;
        }

        public Task AllExecutionsCompleted => _allExecutionsCompleted.Task;

        public int ExecutionCount => _executionCount;

        public int MaxConcurrentExecutions => _maxConcurrentExecutions;

        public async Task<JobExecutionResult> ExecuteAsync(
            JobRecord job,
            CancellationToken cancellationToken)
        {
            var activeExecutions = Interlocked.Increment(ref _activeExecutions);
            TrackMaxConcurrentExecutions(activeExecutions);

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);

                if (Interlocked.Increment(ref _executionCount) == _expectedExecutions)
                {
                    _allExecutionsCompleted.SetResult();
                }

                return JobExecutionResult.Success();
            }
            finally
            {
                Interlocked.Decrement(ref _activeExecutions);
            }
        }

        private void TrackMaxConcurrentExecutions(int activeExecutions)
        {
            while (true)
            {
                var currentMax = _maxConcurrentExecutions;
                if (activeExecutions <= currentMax)
                {
                    return;
                }

                if (Interlocked.CompareExchange(
                    ref _maxConcurrentExecutions,
                    activeExecutions,
                    currentMax) == currentMax)
                {
                    return;
                }
            }
        }
    }

    private sealed class FaultThenSuccessWorker : IJobWorker
    {
        private readonly TaskCompletionSource _successfulExecutionCompleted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public Task SuccessfulExecutionCompleted => _successfulExecutionCompleted.Task;

        public int CallCount => _callCount;

        public Task<bool> ProcessNextJobAsync(string workerId, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                throw new InvalidOperationException("The worker failed.");
            }

            _successfulExecutionCompleted.SetResult();
            return Task.FromResult(false);
        }
    }

    private sealed class LifecycleBackedJobActionDispatcher : IJobActionDispatcher
    {
        private readonly IJobLifecycleService _lifecycle;

        public LifecycleBackedJobActionDispatcher(IJobLifecycleService lifecycle)
        {
            _lifecycle = lifecycle;
        }

        public Task<TResult> DispatchAsync<TResult>(
            IJobActionRequest<TResult> request,
            CancellationToken cancellationToken = default)
        {
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
