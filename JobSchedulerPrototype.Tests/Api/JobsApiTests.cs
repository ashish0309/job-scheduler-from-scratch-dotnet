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
                userId = "user_123"
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
                userId = "user_123"
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
                    type = "",
                    payload = new { }
                },
                "Job type is required."
            }
        };
    }

    private sealed class JobsApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IJobStore>();
                services.AddSingleton<IJobStore, InMemoryJobStore>();
            });
        }
    }
}
