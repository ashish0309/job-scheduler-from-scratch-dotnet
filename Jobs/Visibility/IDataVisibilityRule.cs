using System.Linq.Expressions;

namespace JobSchedulerPrototype.Jobs;

public interface IDataVisibilityRule<TEntity>
    where TEntity : class
{
    DataVisibilityRuleKind Kind { get; }

    Expression<Func<TEntity, bool>> BuildFilter(IDataVisibilityFilterContext context);
}
