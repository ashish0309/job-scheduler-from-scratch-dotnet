using System.Linq.Expressions;

namespace JobSchedulerPrototype.Jobs;

public sealed class OwnerGrantRule<TEntity> : IDataAccessRule<TEntity>
    where TEntity : class, IActorOwned
{
    public DataAccessRuleKind Kind => DataAccessRuleKind.Grant;

    public Expression<Func<TEntity, bool>> BuildFilter(IDataAccessPolicyContext context)
    {
        return entity => entity.CreatedByActorId == context.CurrentActorId;
    }
}
