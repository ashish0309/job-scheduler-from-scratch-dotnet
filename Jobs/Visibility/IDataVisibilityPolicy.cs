using System.Linq.Expressions;

namespace JobSchedulerPrototype.Jobs;

public interface IDataVisibilityPolicy
{
    Type EntityType { get; }

    IReadOnlyCollection<DataAccessOperation> Operations { get; }

    LambdaExpression BuildFilter(IDataVisibilityFilterContext context);
}
