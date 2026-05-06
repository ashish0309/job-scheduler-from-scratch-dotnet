using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class DataVisibilityRuleTranslationTests
{
    [Theory]
    [MemberData(nameof(JobVisibilityRules))]
    public async Task JobVisibilityRulesTranslateWithSqlite(
        IDataVisibilityRule<JobRecord> rule)
    {
        var context = new TestDataVisibilityFilterContext(
            DataAccessScope.Tenant(TestJobActorProvider.TenantId));

        await DataVisibilityTranslationAssert.RuleTranslates(
            rule,
            context);
    }

    [Fact]
    public async Task JobVisibilityPolicyTranslatesWithSqlite()
    {
        var context = new TestDataVisibilityFilterContext(
            DataAccessScope.Tenant(TestJobActorProvider.TenantId));

        await DataVisibilityTranslationAssert.PolicyTranslates(
            new JobVisibilityPolicy(),
            context);
    }

    public static IEnumerable<object[]> JobVisibilityRules()
    {
        return new JobVisibilityPolicy()
            .Rules
            .Select(rule => new object[] { rule });
    }

}
