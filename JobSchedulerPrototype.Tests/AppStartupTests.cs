using JobSchedulerPrototype.Api;
using JobSchedulerPrototype.Jobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace JobSchedulerPrototype.Tests;

public sealed class AppStartupTests
{
    [Fact]
    public void JobActionEndpointsUseDispatchedActionMetadata()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"jobscheduler-{Guid.NewGuid():N}.db");

        try
        {
            using var factory = new SqliteAppFactory($"Data Source={databasePath}");

            var actionEndpoints = factory.Services
                .GetServices<EndpointDataSource>()
                .SelectMany(static endpointDataSource => endpointDataSource.Endpoints)
                .OfType<RouteEndpoint>()
                .Where(static endpoint =>
                    endpoint.RoutePattern.RawText?.StartsWith("/api/jobs", StringComparison.Ordinal) == true)
                .Where(static endpoint =>
                    endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                        .Any(static method =>
                            string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)) == true)
                .ToArray();

            Assert.NotEmpty(actionEndpoints);
            Assert.All(
                actionEndpoints,
                endpoint => Assert.NotNull(
                    endpoint.Metadata.GetMetadata<DispatchedJobActionEndpointMetadata>()));
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public void AppUsesSqliteJobStoreAndCreatesDatabase()
    {
        var databaseDirectory = Path.Combine(
            Path.GetTempPath(),
            $"jobscheduler-{Guid.NewGuid():N}");
        Directory.CreateDirectory(databaseDirectory);
        var databasePath = Path.Combine(
            databaseDirectory,
            $"jobscheduler-{Guid.NewGuid():N}.db");

        try
        {
            using var factory = new SqliteAppFactory($"Data Source={databasePath}");

            using var scope = factory.Services.CreateScope();
            var services = scope.ServiceProvider;

            Assert.IsType<DataAccessScopedJobStore>(services.GetRequiredService<IJobStore>());
            Assert.IsType<DevelopmentHeaderJobActorProvider>(services.GetRequiredService<IJobActorProvider>());
            Assert.IsType<JobAuthorizationRuleEvaluator>(services.GetRequiredService<IJobAuthorizationRuleEvaluator>());
            Assert.IsType<JobActionDispatcher>(services.GetRequiredService<IJobActionDispatcher>());
            Assert.IsType<EnqueueJobAction>(
                services.GetRequiredService<IJobActionHandler<EnqueueJobActionRequest, JobEnqueueResult>>());
            Assert.IsType<ListJobsAction>(
                services.GetRequiredService<IJobActionHandler<ListJobsActionRequest, ListJobsActionResult>>());
            Assert.IsType<GetJobByIdAction>(
                services.GetRequiredService<IJobActionHandler<GetJobByIdActionRequest, GetJobByIdActionResult>>());
            Assert.Contains(
                services.GetServices<IJobEndpointDefinition>(),
                definition => definition is EnqueueJobEndpointDefinition);
            Assert.Contains(
                services.GetServices<IJobEndpointDefinition>(),
                definition => definition is ListJobsEndpointDefinition);
            Assert.Contains(
                services.GetServices<IJobEndpointDefinition>(),
                definition => definition is GetJobByIdEndpointDefinition);
            Assert.IsType<DataAccessScopeProvider>(services.GetRequiredService<IDataAccessScopeProvider>());
            Assert.IsType<DataAccessPolicyFilterBuilder>(services.GetRequiredService<IDataAccessPolicyFilterBuilder>());
            Assert.IsType<JobDataAccessPolicy>(services.GetRequiredService<IDataAccessPolicy>());
            var db = services.GetRequiredService<JobSchedulerDbContext>();
            Assert.True(db.Database.CanConnect());
            Assert.Empty(db.Jobs);
        }
        finally
        {
            if (Directory.Exists(databaseDirectory))
            {
                Directory.Delete(databaseDirectory, recursive: true);
            }
        }
    }

    private sealed class SqliteAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public SqliteAppFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:JobStore", _connectionString);

            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:JobStore"] = _connectionString
                    });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
            });
        }
    }
}
