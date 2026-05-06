using System.Linq.Expressions;

namespace JobSchedulerPrototype.Jobs;

public abstract class DataAccessPolicy<TEntity> : IDataAccessPolicy
    where TEntity : class
{
    public Type EntityType => typeof(TEntity);

    public abstract IReadOnlyList<IDataAccessRule<TEntity>> CommonRules { get; }

    public abstract IReadOnlyDictionary<DataAccessOperation, IReadOnlyList<IDataAccessRule<TEntity>>> RulesByOperation { get; }

    public IReadOnlyCollection<DataAccessOperation> Operations => RulesByOperation.Keys.ToArray();

    public LambdaExpression BuildFilter(IDataAccessPolicyContext context)
    {
        if (!RulesByOperation.TryGetValue(context.Operation, out var operationRules))
        {
            throw new InvalidOperationException(
                $"Data access policy for {typeof(TEntity).Name} does not define rules for operation '{context.Operation}'.");
        }

        return DataAccessRuleExpressionComposer.Compose(
            [.. CommonRules, .. operationRules],
            context);
    }
}
