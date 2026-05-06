using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class DataAccessRuleTranslationTests
{
    [Theory]
    [MemberData(nameof(JobDataAccessRuleCases))]
    public async Task JobDataAccessRulesTranslateWithSqlite(
        DataAccessOperation operation,
        IDataAccessRule<JobRecord> rule)
    {
        var context = new TestDataAccessPolicyContext(
            DataAccessScope.Tenant(TestJobActorProvider.TenantId),
            operation);

        await DataAccessTranslationAssert.RuleTranslates(
            rule,
            context);
    }

    [Theory]
    [MemberData(nameof(JobDataAccessOperations))]
    public async Task JobDataAccessPolicyTranslatesWithSqlite(
        DataAccessOperation operation)
    {
        var context = new TestDataAccessPolicyContext(
            DataAccessScope.Tenant(TestJobActorProvider.TenantId),
            operation);

        await DataAccessTranslationAssert.PolicyTranslates(
            new JobDataAccessPolicy(),
            context);
    }

    public static IEnumerable<object[]> JobDataAccessRuleCases()
    {
        var policy = new JobDataAccessPolicy();

        return policy.RulesByOperation
            .SelectMany(operationRules => policy.CommonRules
                .Concat(operationRules.Value)
                .Select(rule => new object[] { operationRules.Key, rule }));
    }

    public static IEnumerable<object[]> JobDataAccessOperations()
    {
        return new JobDataAccessPolicy()
            .Operations
            .Select(operation => new object[] { operation });
    }
}
