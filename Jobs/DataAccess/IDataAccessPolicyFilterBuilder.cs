using System.Linq.Expressions;

namespace JobSchedulerPrototype.Jobs;

public interface IDataAccessPolicyFilterBuilder
{
    LambdaExpression? BuildFilter(Type entityType, IDataAccessPolicyContext context);
}
