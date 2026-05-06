using System.Linq.Expressions;

namespace JobSchedulerPrototype.Jobs;

public sealed class TenantVisibilityRule<TEntity> : IDataVisibilityRule<TEntity>
    where TEntity : class, ITenantScoped
{
    public DataVisibilityRuleKind Kind => DataVisibilityRuleKind.Boundary;

    public Expression<Func<TEntity, bool>> BuildFilter(IDataVisibilityFilterContext context)
    {
        return entity =>
            context.IncludesAllTenants || entity.TenantId == context.CurrentTenantId;
    }
}
