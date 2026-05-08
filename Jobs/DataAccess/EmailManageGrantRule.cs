using System.Linq.Expressions;

namespace JobSchedulerPrototype.Jobs;

public sealed class EmailManageGrantRule<TEntity> : IDataAccessRule<TEntity>
    where TEntity : class
{
    public DataAccessRuleKind Kind => DataAccessRuleKind.Grant;

    public Expression<Func<TEntity, bool>> BuildFilter(IDataAccessPolicyContext context)
    {
        return _ => context.CanManageEmailJobs;
    }
}
