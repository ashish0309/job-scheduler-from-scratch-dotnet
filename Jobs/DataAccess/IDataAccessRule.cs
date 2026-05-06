using System.Linq.Expressions;

namespace JobSchedulerPrototype.Jobs;

public interface IDataAccessRule<TEntity>
    where TEntity : class
{
    DataAccessRuleKind Kind { get; }

    Expression<Func<TEntity, bool>> BuildFilter(IDataAccessPolicyContext context);
}
