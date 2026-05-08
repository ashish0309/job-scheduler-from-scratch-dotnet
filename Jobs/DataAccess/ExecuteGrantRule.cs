using System.Linq.Expressions;

namespace JobSchedulerPrototype.Jobs;

public sealed class ExecuteGrantRule<TEntity> : IDataAccessRule<TEntity>
    where TEntity : class
{
    public DataAccessRuleKind Kind => DataAccessRuleKind.Grant;

    public Expression<Func<TEntity, bool>> BuildFilter(IDataAccessPolicyContext context)
    {
        return _ => context.CanExecuteJobs;
    }
}
