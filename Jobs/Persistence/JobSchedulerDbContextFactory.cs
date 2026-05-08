using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobSchedulerPrototype.Jobs;

public sealed class JobSchedulerDbContextFactory : IDesignTimeDbContextFactory<JobSchedulerDbContext>
{
    private const string DefaultConnectionString = "Data Source=jobscheduler.db";

    public JobSchedulerDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<JobSchedulerDbContext>();
        optionsBuilder.UseSqlite(DefaultConnectionString);

        return new JobSchedulerDbContext(optionsBuilder.Options);
    }
}
