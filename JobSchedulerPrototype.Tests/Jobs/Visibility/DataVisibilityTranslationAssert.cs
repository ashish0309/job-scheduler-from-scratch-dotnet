using System.Linq.Expressions;
using JobSchedulerPrototype.Jobs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobSchedulerPrototype.Tests.Jobs;

internal static class DataVisibilityTranslationAssert
{
    public static async Task RuleTranslates<TEntity>(
        IDataVisibilityRule<TEntity> rule,
        IDataVisibilityFilterContext context)
        where TEntity : class
    {
        await FilterTranslates(rule.BuildFilter(context));
    }

    public static async Task PolicyTranslates<TEntity>(
        DataVisibilityPolicy<TEntity> policy,
        IDataVisibilityFilterContext context)
        where TEntity : class
    {
        var filter = Assert.IsAssignableFrom<Expression<Func<TEntity, bool>>>(
            policy.BuildFilter(context));

        await FilterTranslates(filter);
    }

    private static async Task FilterTranslates<TEntity>(
        Expression<Func<TEntity, bool>> filter)
        where TEntity : class
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<JobSchedulerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new JobSchedulerDbContext(
            options,
            new FixedDataAccessScopeProvider(DataAccessScope.AllTenants()));
        await db.Database.EnsureCreatedAsync();

        await db.Set<TEntity>().Where(filter).Take(0).ToArrayAsync();
    }
}
