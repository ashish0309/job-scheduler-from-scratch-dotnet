using System.Text.Json;
using JobSchedulerPrototype.Jobs;
using JobSchedulerPrototype.Pages.Jobs;
using JobSchedulerPrototype.Tests.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JobSchedulerPrototype.Tests.Pages;

public sealed class JobDetailsModelTests
{
    [Fact]
    public async Task OnGetLoadsJobDetails()
    {
        var store = new InMemoryJobStore();
        var job = JobRecord.Enqueue(
            Guid.NewGuid(),
            TestJobActorProvider.TenantId,
            TestJobActorProvider.ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));
        store.Add(job);
        var runningJob = store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero), "worker-1", (new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero)).AddMinutes(1));
        Assert.NotNull(runningJob);
        store.MarkCompleted(job.Id, runningJob.CurrentStateChangeId);
        var model = new DetailsModel(new StoreBackedJobActionDispatcher(store));

        var result = await model.OnGet(job.Id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.Job);
        Assert.Equal(job.Id, model.Job.Id);
        Assert.Equal(TestJobActorProvider.TenantId, model.Job.TenantId);
        Assert.Equal(TestJobActorProvider.ActorId, model.Job.CreatedByActorId);
        Assert.Equal("send-welcome-email", model.Job.Type);
        Assert.Equal(JobStatus.Completed, model.Job.Status);
        Assert.Null(model.Job.ClaimedBy);
        Assert.Null(model.Job.ClaimedAt);
        Assert.Null(model.Job.LeaseExpiresAt);
        Assert.Null(model.Job.AcknowledgedBy);
        Assert.Null(model.Job.AcknowledgedAt);
        Assert.Equal("""{"userId":"user_123","email":"person@example.com"}""", model.Job.Payload);
        Assert.Equal(1, model.Job.AttemptCount);
        Assert.Equal(3, model.Job.MaxAttempts);
        Assert.Single(model.Job.Attempts);
        Assert.Equal(3, model.Job.History.Count);
        Assert.Equal(model.Job.CurrentStateChangeId, model.Job.History[^1].Id);
        Assert.Equal($"/api/jobs/{job.Id}", model.Job.StatusUrl);
    }

    [Fact]
    public async Task OnGetReturnsNotFoundWhenJobDoesNotExist()
    {
        var model = new DetailsModel(new StoreBackedJobActionDispatcher(new InMemoryJobStore()));

        var result = await model.OnGet(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        Assert.Null(model.Job);
    }

    [Fact]
    public async Task OnGetReturnsForbidWhenActorIsUnauthorized()
    {
        var store = new InMemoryJobStore();
        var model = new DetailsModel(new StoreBackedJobActionDispatcher(store, isAuthorized: false));

        var result = await model.OnGet(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.Null(model.Job);
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse(
            """{"userId":"user_123","email":"person@example.com"}""");
        return document.RootElement.Clone();
    }

    private sealed class StoreBackedJobActionDispatcher : IJobActionDispatcher
    {
        private readonly IJobStore _store;
        private readonly bool _isAuthorized;

        public StoreBackedJobActionDispatcher(
            IJobStore store,
            bool isAuthorized = true)
        {
            _store = store;
            _isAuthorized = isAuthorized;
        }

        public Task<TResult> DispatchAsync<TResult>(
            IJobActionRequest<TResult> request,
            CancellationToken cancellationToken = default)
        {
            object response = request switch
            {
                GetJobByIdActionRequest getByIdRequest when _isAuthorized =>
                    GetJobByIdActionResult.Authorized(_store.Get(getByIdRequest.Id)),
                GetJobByIdActionRequest =>
                    GetJobByIdActionResult.Denied("Actor is not authorized to read jobs."),
                _ => throw new InvalidOperationException(
                    $"Unsupported request type '{request.GetType().Name}' in test dispatcher.")
            };

            return Task.FromResult((TResult)response);
        }
    }
}
