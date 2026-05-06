using System.Linq.Expressions;

namespace JobSchedulerPrototype.Jobs;

public abstract class DataVisibilityPolicy<TEntity> : IDataVisibilityPolicy
    where TEntity : class
{
    public Type EntityType => typeof(TEntity);

    public abstract IReadOnlyList<IDataVisibilityRule<TEntity>> CommonRules { get; }

    public abstract IReadOnlyDictionary<DataAccessOperation, IReadOnlyList<IDataVisibilityRule<TEntity>>> RulesByOperation { get; }

    public IReadOnlyCollection<DataAccessOperation> Operations => RulesByOperation.Keys.ToArray();

    public LambdaExpression BuildFilter(IDataVisibilityFilterContext context)
    {
        if (!RulesByOperation.TryGetValue(context.Operation, out var operationRules))
        {
            throw new InvalidOperationException(
                $"Data visibility policy for {typeof(TEntity).Name} does not define rules for operation '{context.Operation}'.");
        }

        return DataVisibilityExpressionComposer.Compose(
            [.. CommonRules, .. operationRules],
            context);
    }
}
