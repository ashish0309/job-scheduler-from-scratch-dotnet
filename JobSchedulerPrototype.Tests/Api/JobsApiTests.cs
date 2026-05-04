using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobSchedulerPrototype.Api;
using JobSchedulerPrototype.Jobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace JobSchedulerPrototype.Tests.Api;

public sealed class JobsApiTests
{
    [Fact]
    public async Task PostJobsAcceptsSupportedJob()
    {
        await using var factory = new JobsApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/jobs", new
        {
            type = "send-welcome-email",
            payload = new
            {
                userId = "user_123",
                email = "person@example.com"
            }
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JobResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.Equal("send-welcome-email", body.Type);
        Assert.Equal("Queued", body.Status);
        Assert.Null(body.FailureReason);
        Assert.Null(body.StartedAt);
        Assert.Null(body.CompletedAt);
        Assert.Null(body.FailedAt);
        Assert.Equal(0, body.AttemptCount);
        Assert.Equal(3, body.MaxAttempts);
        Assert.False(body.RetryAvailable);
        Assert.Equal($"/api/jobs/{body.Id}", body.StatusUrl);
        Assert.Equal(new Uri(body.StatusUrl, UriKind.Relative), response.Headers.Location);
    }

    [Fact]
    public async Task GetJobsListsEnqueuedJobs()
    {
        await using var factory = new JobsApiFactory();
        using var client = factory.CreateClient();

        var postResponse = await client.PostAsJsonAsync("/api/jobs", new
        {
            type = "send-welcome-email",
            payload = new
            {
                userId = "user_123",
                email = "person@example.com"
            }
        });
        var postedJob = await postResponse.Content.ReadFromJsonAsync<JobResponse>();

        var response = await client.GetAsync("/api/jobs");

        response.EnsureSuccessStatusCode();
        var jobs = await response.Content.ReadFromJsonAsync<JobResponse[]>();

        Assert.NotNull(postedJob);
        Assert.NotNull(jobs);
        var job = Assert.Single(jobs);
        Assert.Equal(postedJob.Id, job.Id);
        Assert.Equal("send-welcome-email", job.Type);
        Assert.Equal("Queued", job.Status);
        Assert.Null(job.FailureReason);
        Assert.Null(job.StartedAt);
        Assert.Null(job.CompletedAt);
        Assert.Null(job.FailedAt);
        Assert.Equal(0, job.AttemptCount);
        Assert.Equal(3, job.MaxAttempts);
        Assert.False(job.RetryAvailable);
        Assert.Equal($"/api/jobs/{postedJob.Id}", job.StatusUrl);
    }

    [Fact]
    public async Task GetJobReturnsJobById()
    {
        await using var factory = new JobsApiFactory();
        using var client = factory.CreateClient();

        var postResponse = await client.PostAsJsonAsync("/api/jobs", new
        {
            type = "send-welcome-email",
            payload = new
            {
                userId = "user_123",
                email = "person@example.com"
            }
        });
        var postedJob = await postResponse.Content.ReadFromJsonAsync<JobResponse>();

        var response = await client.GetAsync(postedJob?.StatusUrl);

        response.EnsureSuccessStatusCode();
        var job = await response.Content.ReadFromJsonAsync<JobResponse>();

        Assert.NotNull(postedJob);
        Assert.NotNull(job);
        Assert.Equal(postedJob.Id, job.Id);
        Assert.Equal(postedJob.StatusUrl, job.StatusUrl);
    }

    [Fact]
    public async Task GetJobReturnsFailureReasonWhenJobFailed()
    {
        var jobId = Guid.NewGuid();
        await using var factory = new JobsApiFactory(store =>
        {
            var job = JobRecord.Enqueue(
                jobId,
                "send-welcome-email",
                Payload(),
                maxAttempts: 3,
                new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));

            store.Add(job);
            store.TryClaimNextQueuedJob();
            store.MarkFailed(jobId, "SMTP server unavailable.");
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/jobs/{jobId}");

        response.EnsureSuccessStatusCode();
        var job = await response.Content.ReadFromJsonAsync<JobResponse>();

        Assert.NotNull(job);
        Assert.Equal(jobId, job.Id);
        Assert.Equal("Failed", job.Status);
        Assert.Equal("SMTP server unavailable.", job.FailureReason);
        Assert.Equal(new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero), job.EnqueuedAt);
        Assert.NotNull(job.StartedAt);
        Assert.Null(job.CompletedAt);
        Assert.NotNull(job.FailedAt);
        Assert.Equal(1, job.AttemptCount);
        Assert.Equal(3, job.MaxAttempts);
        Assert.True(job.RetryAvailable);
    }

    [Fact]
    public async Task GetJobReturnsLifecycleTimestampsWhenJobCompleted()
    {
        var jobId = Guid.NewGuid();
        await using var factory = new JobsApiFactory(store =>
        {
            var job = JobRecord.Enqueue(
                jobId,
                "send-welcome-email",
                Payload(),
                maxAttempts: 3,
                new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));

            store.Add(job);
            store.TryClaimNextQueuedJob();
            store.MarkCompleted(jobId);
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/jobs/{jobId}");

        response.EnsureSuccessStatusCode();
        var job = await response.Content.ReadFromJsonAsync<JobResponse>();

        Assert.NotNull(job);
        Assert.Equal(jobId, job.Id);
        Assert.Equal("Completed", job.Status);
        Assert.Equal(new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero), job.EnqueuedAt);
        Assert.NotNull(job.StartedAt);
        Assert.NotNull(job.CompletedAt);
        Assert.Null(job.FailedAt);
        Assert.Equal(1, job.AttemptCount);
        Assert.Equal(3, job.MaxAttempts);
        Assert.False(job.RetryAvailable);
    }

    [Fact]
    public async Task RetryJobMovesEligibleFailedJobBackToQueued()
    {
        var jobId = Guid.NewGuid();
        await using var factory = new JobsApiFactory(store =>
        {
            var job = JobRecord.Enqueue(
                jobId,
                "send-welcome-email",
                Payload(),
                maxAttempts: 3,
                new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));

            store.Add(job);
            store.TryClaimNextQueuedJob();
            store.MarkFailed(jobId, "SMTP server unavailable.");
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/api/jobs/{jobId}/retry", content: null);

        response.EnsureSuccessStatusCode();
        var job = await response.Content.ReadFromJsonAsync<JobResponse>();
        Assert.NotNull(job);
        Assert.Equal(jobId, job.Id);
        Assert.Equal("Queued", job.Status);
        Assert.Null(job.FailureReason);
        Assert.Equal(1, job.AttemptCount);
        Assert.Equal(3, job.MaxAttempts);
        Assert.False(job.RetryAvailable);
    }

    [Fact]
    public async Task RetryJobRejectsIneligibleJobs()
    {
        var jobId = Guid.NewGuid();
        await using var factory = new JobsApiFactory(store =>
        {
            var job = JobRecord.Enqueue(
                jobId,
                "send-welcome-email",
                Payload(),
                maxAttempts: 1,
                new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));

            store.Add(job);
            store.TryClaimNextQueuedJob();
            store.MarkFailed(jobId, "SMTP server unavailable.");
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/api/jobs/{jobId}/retry", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JobValidationError>();
        Assert.NotNull(error);
        Assert.Equal("Job is not eligible for retry.", error.Message);
    }

    [Fact]
    public async Task RetryJobReturnsNotFoundWhenJobDoesNotExist()
    {
        await using var factory = new JobsApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/api/jobs/{Guid.NewGuid()}/retry", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetJobReturnsNotFoundWhenJobDoesNotExist()
    {
        await using var factory = new JobsApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/jobs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task PostJobsRejectsInvalidRequests(object request, string expectedMessage)
    {
        await using var factory = new JobsApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/jobs", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JobValidationError>();
        Assert.NotNull(body);
        Assert.Equal(expectedMessage, body.Message);

        var listResponse = await client.GetAsync("/api/jobs");
        listResponse.EnsureSuccessStatusCode();
        var jobs = await listResponse.Content.ReadFromJsonAsync<JobResponse[]>();
        Assert.NotNull(jobs);
        Assert.Empty(jobs);
    }

    public static TheoryData<object, string> InvalidRequests()
    {
        return new TheoryData<object, string>
        {
            {
                new
                {
                    type = "not-a-real-job",
                    payload = new { }
                },
                "Unsupported job type."
            },
            {
                new
                {
                    type = "send-welcome-email",
                    payload = (JsonElement?)null
                },
                "Job payload is required."
            },
            {
                new
                {
                    type = "send-welcome-email",
                    payload = new
                    {
                        email = "person@example.com"
                    }
                },
                "User ID is required."
            },
            {
                new
                {
                    type = "send-welcome-email",
                    payload = new
                    {
                        userId = "user_123"
                    }
                },
                "Email is required."
            },
            {
                new
                {
                    type = "",
                    payload = new { }
                },
                "Job type is required."
            }
        };
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse("""{"userId":"user_123","email":"person@example.com"}""");
        return document.RootElement.Clone();
    }

    private sealed class JobsApiFactory : WebApplicationFactory<Program>
    {
        private readonly Action<IJobStore>? _configureStore;

        public JobsApiFactory(Action<IJobStore>? configureStore = null)
        {
            _configureStore = configureStore;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IJobStore>();
                services.AddSingleton<IJobStore>(_ =>
                {
                    var store = new InMemoryJobStore();
                    _configureStore?.Invoke(store);
                    return store;
                });
            });
        }
    }
}
