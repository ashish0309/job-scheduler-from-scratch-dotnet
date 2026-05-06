using System.Linq.Expressions;

namespace JobSchedulerPrototype.Jobs;

public sealed class DataAccessPolicyFilterBuilder : IDataAccessPolicyFilterBuilder
{
    private readonly IReadOnlyDictionary<Type, IDataAccessPolicy> _policiesByEntityType;

    public DataAccessPolicyFilterBuilder(IEnumerable<IDataAccessPolicy> policies)
    {
        _policiesByEntityType = policies.ToDictionary(policy => policy.EntityType);
    }

    public LambdaExpression? BuildFilter(Type entityType, IDataAccessPolicyContext context)
    {
        return _policiesByEntityType.TryGetValue(entityType, out var policy)
            ? policy.BuildFilter(context)
            : null;
    }
}

internal static class DataAccessRuleExpressionComposer
{
    public static Expression<Func<TEntity, bool>> Compose<TEntity>(
        IReadOnlyList<IDataAccessRule<TEntity>> rules,
        IDataAccessPolicyContext context)
        where TEntity : class
    {
        if (rules.Count == 0)
        {
            throw new InvalidOperationException(
                $"Data access policy for {typeof(TEntity).Name} must define at least one rule.");
        }

        var boundaries = rules
            .Where(rule => rule.Kind == DataAccessRuleKind.Boundary)
            .Select(rule => rule.BuildFilter(context))
            .ToArray();
        var grants = rules
            .Where(rule => rule.Kind == DataAccessRuleKind.Grant)
            .Select(rule => rule.BuildFilter(context))
            .ToArray();

        var filter = CombineAnd(boundaries) ?? AllowAll<TEntity>();
        var grantFilter = CombineOr(grants);

        return grantFilter is null
            ? filter
            : Combine(filter, grantFilter, Expression.AndAlso);
    }

    private static Expression<Func<TEntity, bool>>? CombineAnd<TEntity>(
        IReadOnlyList<Expression<Func<TEntity, bool>>> filters)
    {
        return filters.Count == 0
            ? null
            : filters.Aggregate((left, right) => Combine(left, right, Expression.AndAlso));
    }

    private static Expression<Func<TEntity, bool>>? CombineOr<TEntity>(
        IReadOnlyList<Expression<Func<TEntity, bool>>> filters)
    {
        return filters.Count == 0
            ? null
            : filters.Aggregate((left, right) => Combine(left, right, Expression.OrElse));
    }

    private static Expression<Func<TEntity, bool>> Combine<TEntity>(
        Expression<Func<TEntity, bool>> left,
        Expression<Func<TEntity, bool>> right,
        Func<Expression, Expression, BinaryExpression> merge)
    {
        var parameter = left.Parameters[0];
        var rightBody = new ParameterReplacingExpressionVisitor(
                right.Parameters[0],
                parameter)
            .Visit(right.Body)!;

        return Expression.Lambda<Func<TEntity, bool>>(
            merge(left.Body, rightBody),
            parameter);
    }

    private static Expression<Func<TEntity, bool>> AllowAll<TEntity>()
    {
        return _ => true;
    }

    private sealed class ParameterReplacingExpressionVisitor : ExpressionVisitor
    {
        private readonly ParameterExpression _source;
        private readonly ParameterExpression _target;

        public ParameterReplacingExpressionVisitor(
            ParameterExpression source,
            ParameterExpression target)
        {
            _source = source;
            _target = target;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == _source
                ? _target
                : node;
        }
    }
}
