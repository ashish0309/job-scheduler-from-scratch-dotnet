using System.Text.Json;
using JobSchedulerPrototype.Jobs;
using JobSchedulerPrototype.Pages.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JobSchedulerPrototype.Tests.Pages;

public sealed class JobDetailsModelTests
{
    [Fact]
    public void OnGetLoadsJobDetails()
    {
        var store = new InMemoryJobStore();
        var job = JobRecord.Enqueue(
            Guid.NewGuid(),
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));
        store.Add(job);
        store.TryClaimNextDueJob(new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero), "worker-1", (new DateTimeOffset(2026, 5, 4, 10, 5, 0, TimeSpan.Zero)).AddMinutes(1));
        store.MarkCompleted(job.Id);
        var model = new DetailsModel(store);

        var result = model.OnGet(job.Id);

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.Job);
        Assert.Equal(job.Id, model.Job.Id);
        Assert.Equal("send-welcome-email", model.Job.Type);
        Assert.Equal(JobStatus.Completed, model.Job.Status);
        Assert.Null(model.Job.ClaimedBy);
        Assert.Null(model.Job.ClaimedAt);
        Assert.Null(model.Job.LeaseExpiresAt);
        Assert.Equal("""{"userId":"user_123","email":"person@example.com"}""", model.Job.Payload);
        Assert.Equal(1, model.Job.AttemptCount);
        Assert.Equal(3, model.Job.MaxAttempts);
        Assert.Single(model.Job.Attempts);
        Assert.Equal(3, model.Job.History.Count);
        Assert.Equal(model.Job.CurrentStateChangeId, model.Job.History[^1].Id);
        Assert.Equal($"/api/jobs/{job.Id}", model.Job.StatusUrl);
    }

    [Fact]
    public void OnGetReturnsNotFoundWhenJobDoesNotExist()
    {
        var model = new DetailsModel(new InMemoryJobStore());

        var result = model.OnGet(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
        Assert.Null(model.Job);
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse(
            """{"userId":"user_123","email":"person@example.com"}""");
        return document.RootElement.Clone();
    }
}
