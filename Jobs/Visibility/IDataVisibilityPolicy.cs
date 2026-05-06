using System.Linq.Expressions;

namespace JobSchedulerPrototype.Jobs;

public interface IDataVisibilityPolicy
{
    Type EntityType { get; }

    LambdaExpression BuildFilter(IDataVisibilityFilterContext context);
}
