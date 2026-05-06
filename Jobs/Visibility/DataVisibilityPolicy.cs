using System.Linq.Expressions;

namespace JobSchedulerPrototype.Jobs;

public abstract class DataVisibilityPolicy<TEntity> : IDataVisibilityPolicy
    where TEntity : class
{
    public Type EntityType => typeof(TEntity);

    public abstract IReadOnlyList<IDataVisibilityRule<TEntity>> Rules { get; }

    public LambdaExpression BuildFilter(IDataVisibilityFilterContext context)
    {
        return DataVisibilityExpressionComposer.Compose(Rules, context);
    }
}
