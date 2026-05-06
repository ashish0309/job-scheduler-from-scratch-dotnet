using System.Linq.Expressions;

namespace JobSchedulerPrototype.Jobs;

public interface IDataAccessPolicy
{
    Type EntityType { get; }

    IReadOnlyCollection<DataAccessOperation> Operations { get; }

    LambdaExpression BuildFilter(IDataAccessPolicyContext context);
}
