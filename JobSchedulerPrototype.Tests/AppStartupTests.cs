using JobSchedulerPrototype.Jobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace JobSchedulerPrototype.Tests;

public sealed class AppStartupTests
{
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

            Assert.IsType<SqliteJobStore>(services.GetRequiredService<IJobStore>());
            Assert.IsType<DevelopmentHeaderJobActorProvider>(services.GetRequiredService<IJobActorProvider>());
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
