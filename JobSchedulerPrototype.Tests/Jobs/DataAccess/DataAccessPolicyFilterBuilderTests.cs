using System.Linq.Expressions;
using System.Text.Json;
using JobSchedulerPrototype.Jobs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class DataAccessPolicyFilterBuilderTests
{
    [Fact]
    public void BuildFilterUsesPolicyRegisteredForEntityType()
    {
        var builder = new DataAccessPolicyFilterBuilder([new JobDataAccessPolicy()]);
        var context = new TestDataAccessPolicyContext(
            DataAccessScope.Tenant(TestJobActorProvider.TenantId));

        var filter = builder.BuildFilter(typeof(JobRecord), context);

        var typedFilter = Assert.IsAssignableFrom<Expression<Func<JobRecord, bool>>>(filter);
        var canSee = typedFilter.Compile();
        Assert.True(canSee(CreateQueuedJob(TestJobActorProvider.TenantId)));
        Assert.False(canSee(CreateQueuedJob("tenant-beta")));
    }

    [Fact]
    public void ReadFilterAllowsOnlyOwnedJobsWithoutManagePermission()
    {
        var builder = new DataAccessPolicyFilterBuilder([new JobDataAccessPolicy()]);
        var actor = new JobActor(
            id: "owner-alpha",
            tenantId: TestJobActorProvider.TenantId,
            permissions: [JobPermissions.EmailRead]);
        var context = new TestDataAccessPolicyContext(
            DataAccessScope.Tenant(TestJobActorProvider.TenantId),
            DataAccessOperation.Read,
            actor);

        var filter = builder.BuildFilter(typeof(JobRecord), context);

        var typedFilter = Assert.IsAssignableFrom<Expression<Func<JobRecord, bool>>>(filter);
        var canSee = typedFilter.Compile();
        Assert.True(canSee(CreateQueuedJob(TestJobActorProvider.TenantId, actor.Id)));
        Assert.False(canSee(CreateQueuedJob(TestJobActorProvider.TenantId, "owner-beta")));
    }

    [Fact]
    public void ReadFilterAllowsAllTenantJobsWithManagePermission()
    {
        var builder = new DataAccessPolicyFilterBuilder([new JobDataAccessPolicy()]);
        var actor = new JobActor(
            id: "manager-alpha",
            tenantId: TestJobActorProvider.TenantId,
            permissions: [JobPermissions.EmailRead, JobPermissions.EmailManage]);
        var context = new TestDataAccessPolicyContext(
            DataAccessScope.Tenant(TestJobActorProvider.TenantId),
            DataAccessOperation.Read,
            actor);

        var filter = builder.BuildFilter(typeof(JobRecord), context);

        var typedFilter = Assert.IsAssignableFrom<Expression<Func<JobRecord, bool>>>(filter);
        var canSee = typedFilter.Compile();
        Assert.True(canSee(CreateQueuedJob(TestJobActorProvider.TenantId, "owner-alpha")));
        Assert.True(canSee(CreateQueuedJob(TestJobActorProvider.TenantId, "owner-beta")));
        Assert.False(canSee(CreateQueuedJob("tenant-beta", "owner-gamma")));
    }

    [Fact]
    public void ReadFilterAllowsCrossTenantJobsWithGlobalReadPermissionAndAllTenantsScope()
    {
        var builder = new DataAccessPolicyFilterBuilder([new JobDataAccessPolicy()]);
        var actor = new JobActor(
            id: "service-user",
            tenantId: TestJobActorProvider.TenantId,
            permissions: [JobPermissions.EmailRead, JobPermissions.GlobalRead]);
        var context = new TestDataAccessPolicyContext(
            DataAccessScope.AllTenants(),
            DataAccessOperation.Read,
            actor);

        var filter = builder.BuildFilter(typeof(JobRecord), context);

        var typedFilter = Assert.IsAssignableFrom<Expression<Func<JobRecord, bool>>>(filter);
        var canSee = typedFilter.Compile();
        Assert.True(canSee(CreateQueuedJob(TestJobActorProvider.TenantId, "owner-alpha")));
        Assert.True(canSee(CreateQueuedJob("tenant-beta", "owner-gamma")));
    }

    [Fact]
    public void MutateFilterAllowsOnlyOwnedJobsWithoutManageOrExecutePermission()
    {
        var builder = new DataAccessPolicyFilterBuilder([new JobDataAccessPolicy()]);
        var actor = new JobActor(
            id: "owner-alpha",
            tenantId: TestJobActorProvider.TenantId,
            permissions: [JobPermissions.EmailRead]);
        var context = new TestDataAccessPolicyContext(
            DataAccessScope.Tenant(TestJobActorProvider.TenantId),
            DataAccessOperation.Mutate,
            actor);

        var filter = builder.BuildFilter(typeof(JobRecord), context);

        var typedFilter = Assert.IsAssignableFrom<Expression<Func<JobRecord, bool>>>(filter);
        var canMutate = typedFilter.Compile();
        Assert.True(canMutate(CreateQueuedJob(TestJobActorProvider.TenantId, actor.Id)));
        Assert.False(canMutate(CreateQueuedJob(TestJobActorProvider.TenantId, "owner-beta")));
    }

    [Fact]
    public void MutateFilterAllowsAllTenantJobsWithManagePermission()
    {
        var builder = new DataAccessPolicyFilterBuilder([new JobDataAccessPolicy()]);
        var actor = new JobActor(
            id: "manager-alpha",
            tenantId: TestJobActorProvider.TenantId,
            permissions: [JobPermissions.EmailManage]);
        var context = new TestDataAccessPolicyContext(
            DataAccessScope.Tenant(TestJobActorProvider.TenantId),
            DataAccessOperation.Mutate,
            actor);

        var filter = builder.BuildFilter(typeof(JobRecord), context);

        var typedFilter = Assert.IsAssignableFrom<Expression<Func<JobRecord, bool>>>(filter);
        var canMutate = typedFilter.Compile();
        Assert.True(canMutate(CreateQueuedJob(TestJobActorProvider.TenantId, "owner-alpha")));
        Assert.True(canMutate(CreateQueuedJob(TestJobActorProvider.TenantId, "owner-beta")));
        Assert.False(canMutate(CreateQueuedJob("tenant-beta", "owner-gamma")));
    }

    [Fact]
    public void MutateFilterAllowsCrossTenantJobsWithExecutePermissionAndAllTenantsScope()
    {
        var builder = new DataAccessPolicyFilterBuilder([new JobDataAccessPolicy()]);
        var actor = new JobActor(
            id: "worker-service",
            tenantId: TestJobActorProvider.TenantId,
            permissions: [JobPermissions.Execute]);
        var context = new TestDataAccessPolicyContext(
            DataAccessScope.AllTenants(),
            DataAccessOperation.Mutate,
            actor);

        var filter = builder.BuildFilter(typeof(JobRecord), context);

        var typedFilter = Assert.IsAssignableFrom<Expression<Func<JobRecord, bool>>>(filter);
        var canMutate = typedFilter.Compile();
        Assert.True(canMutate(CreateQueuedJob(TestJobActorProvider.TenantId, "owner-alpha")));
        Assert.True(canMutate(CreateQueuedJob("tenant-beta", "owner-gamma")));
    }

    [Fact]
    public void BuildFilterReturnsNullWhenEntityHasNoPolicy()
    {
        var builder = new DataAccessPolicyFilterBuilder([new JobDataAccessPolicy()]);
        var context = new TestDataAccessPolicyContext(
            DataAccessScope.Tenant(TestJobActorProvider.TenantId));

        var filter = builder.BuildFilter(typeof(JobStateChange), context);

        Assert.Null(filter);
    }

    [Fact]
    public void BuildFilterThrowsWhenPolicyDoesNotDefineCurrentOperation()
    {
        var builder = new DataAccessPolicyFilterBuilder([new JobDataAccessPolicy()]);
        var context = new TestDataAccessPolicyContext(
            DataAccessScope.Tenant(TestJobActorProvider.TenantId),
            (DataAccessOperation)999);

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.BuildFilter(typeof(JobRecord), context));

        Assert.Contains("does not define rules for operation '999'", exception.Message);
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

    private static JobRecord CreateQueuedJob(
        string tenantId,
        string createdByActorId = TestJobActorProvider.ActorId)
    {
        return JobRecord.Enqueue(
            Guid.NewGuid(),
            tenantId,
            createdByActorId,
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
