using System.Linq.Expressions;

namespace JobSchedulerPrototype.Jobs;

public interface IDataVisibilityFilterBuilder
{
    LambdaExpression? BuildFilter(Type entityType, IDataVisibilityFilterContext context);
}
