using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class DataVisibilityRuleTranslationTests
{
    [Theory]
    [MemberData(nameof(JobVisibilityRuleCases))]
    public async Task JobVisibilityRulesTranslateWithSqlite(
        DataAccessOperation operation,
        IDataVisibilityRule<JobRecord> rule)
    {
        var context = new TestDataVisibilityFilterContext(
            DataAccessScope.Tenant(TestJobActorProvider.TenantId),
            operation);

        await DataVisibilityTranslationAssert.RuleTranslates(
            rule,
            context);
    }

    [Theory]
    [MemberData(nameof(JobVisibilityOperations))]
    public async Task JobVisibilityPolicyTranslatesWithSqlite(
        DataAccessOperation operation)
    {
        var context = new TestDataVisibilityFilterContext(
            DataAccessScope.Tenant(TestJobActorProvider.TenantId),
            operation);

        await DataVisibilityTranslationAssert.PolicyTranslates(
            new JobVisibilityPolicy(),
            context);
    }

    public static IEnumerable<object[]> JobVisibilityRuleCases()
    {
        var policy = new JobVisibilityPolicy();

        return policy.RulesByOperation
            .SelectMany(operationRules => policy.CommonRules
                .Concat(operationRules.Value)
                .Select(rule => new object[] { operationRules.Key, rule }));
    }

    public static IEnumerable<object[]> JobVisibilityOperations()
    {
        return new JobVisibilityPolicy()
            .Operations
            .Select(operation => new object[] { operation });
    }
}
