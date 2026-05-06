using System.Linq.Expressions;

namespace JobSchedulerPrototype.Jobs;

public sealed class TenantBoundaryRule<TEntity> : IDataAccessRule<TEntity>
    where TEntity : class, ITenantScoped
{
    public DataAccessRuleKind Kind => DataAccessRuleKind.Boundary;

    public Expression<Func<TEntity, bool>> BuildFilter(IDataAccessPolicyContext context)
    {
        return entity =>
            context.IncludesAllTenants || entity.TenantId == context.CurrentTenantId;
    }
}
