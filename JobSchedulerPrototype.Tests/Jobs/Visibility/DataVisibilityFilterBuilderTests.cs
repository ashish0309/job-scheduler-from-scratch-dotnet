using System.Linq.Expressions;
using System.Text.Json;
using JobSchedulerPrototype.Jobs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class DataVisibilityFilterBuilderTests
{
    [Fact]
    public void BuildFilterUsesPolicyRegisteredForEntityType()
    {
        var builder = new DataVisibilityFilterBuilder([new JobVisibilityPolicy()]);
        var context = new TestDataVisibilityFilterContext(
            DataAccessScope.Tenant(TestJobActorProvider.TenantId));

        var filter = builder.BuildFilter(typeof(JobRecord), context);

        var typedFilter = Assert.IsAssignableFrom<Expression<Func<JobRecord, bool>>>(filter);
        var canSee = typedFilter.Compile();
        Assert.True(canSee(CreateQueuedJob(TestJobActorProvider.TenantId)));
        Assert.False(canSee(CreateQueuedJob("tenant-beta")));
    }

    [Fact]
    public void BuildFilterReturnsNullWhenEntityHasNoPolicy()
    {
        var builder = new DataVisibilityFilterBuilder([new JobVisibilityPolicy()]);
        var context = new TestDataVisibilityFilterContext(
            DataAccessScope.Tenant(TestJobActorProvider.TenantId));

        var filter = builder.BuildFilter(typeof(JobStateChange), context);

        Assert.Null(filter);
    }

    [Fact]
    public async Task FilterWithServiceMethodCompilesButFailsWhenEfTranslatesQuery()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<JobSchedulerDbContext>()
            .UseSqlite(connection)
            .Options;

        var actor = new JobActor(
            TestJobActorProvider.ActorId,
            TestJobActorProvider.TenantId,
            [JobPermissions.All]);
        var authorizationService = new TestAuthorizationService();
        Expression<Func<JobRecord, bool>> filter =
            job => authorizationService.CanSeeJob(actor, job);

        await using var db = new JobSchedulerDbContext(
            options,
            new FixedDataAccessScopeProvider(DataAccessScope.AllTenants()));
        await db.Database.EnsureCreatedAsync();
        db.Jobs.Add(CreateQueuedJob(TestJobActorProvider.TenantId));
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.Jobs.Where(filter).ToArrayAsync());

        Assert.Contains("could not be translated", exception.Message);
    }

    private static JobRecord CreateQueuedJob(string tenantId)
    {
        return JobRecord.Enqueue(
            Guid.NewGuid(),
            tenantId,
            TestJobActorProvider.ActorId,
            "send-welcome-email",
            Payload(),
            maxAttempts: 3,
            new DateTimeOffset(2026, 5, 6, 10, 0, 0, TimeSpan.Zero));
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse(
            """{"userId":"user_123","email":"person@example.com"}""");
        return document.RootElement.Clone();
    }

    private sealed class TestAuthorizationService
    {
        public bool CanSeeJob(JobActor actor, JobRecord job)
        {
            return actor.TenantId == job.TenantId;
        }
    }
}
