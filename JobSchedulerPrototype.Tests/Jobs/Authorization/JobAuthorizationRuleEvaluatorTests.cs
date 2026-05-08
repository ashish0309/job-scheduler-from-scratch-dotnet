using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Tests.Jobs;

public sealed class JobAuthorizationRuleEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsyncRunsDenyRulesBeforeGrantRules()
    {
        var callOrder = new List<string>();
        var actor = new JobActor("user-1", "tenant-alpha", [JobPermissions.EmailRead]);
        var evaluator = new JobAuthorizationRuleEvaluator();
        var rules = new IJobAuthorizationRule[]
        {
            new TestRule(
                name: "grant-first",
                kind: JobAuthorizationRuleKind.Grant,
                result: JobAuthorizationResult.Allow(),
                callOrder),
            new TestRule(
                name: "deny-second",
                kind: JobAuthorizationRuleKind.Deny,
                result: JobAuthorizationResult.Deny("blocked"),
                callOrder),
            new TestRule(
                name: "grant-third",
                kind: JobAuthorizationRuleKind.Grant,
                result: JobAuthorizationResult.Allow(),
                callOrder)
        };

        var result = await evaluator.EvaluateAsync(actor, rules, CancellationToken.None);

        Assert.False(result.IsAuthorized);
        Assert.Equal("blocked", result.ErrorMessage);
        Assert.Equal(["deny-second"], callOrder);
    }

    [Fact]
    public async Task EvaluateAsyncRunsGrantRulesWhenDenyRulesPass()
    {
        var callOrder = new List<string>();
        var actor = new JobActor("user-1", "tenant-alpha", [JobPermissions.EmailRead]);
        var evaluator = new JobAuthorizationRuleEvaluator();
        var rules = new IJobAuthorizationRule[]
        {
            new TestRule(
                name: "grant-first",
                kind: JobAuthorizationRuleKind.Grant,
                result: JobAuthorizationResult.Allow(),
                callOrder),
            new TestRule(
                name: "deny-second",
                kind: JobAuthorizationRuleKind.Deny,
                result: JobAuthorizationResult.Allow(),
                callOrder),
            new TestRule(
                name: "grant-third",
                kind: JobAuthorizationRuleKind.Grant,
                result: JobAuthorizationResult.Allow(),
                callOrder)
        };

        var result = await evaluator.EvaluateAsync(actor, rules, CancellationToken.None);

        Assert.True(result.IsAuthorized);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(["deny-second", "grant-first", "grant-third"], callOrder);
    }

    [Fact]
    public async Task EvaluateAsyncDeniesWhenNoGrantRuleAllows()
    {
        var callOrder = new List<string>();
        var actor = new JobActor("user-1", "tenant-alpha", [JobPermissions.EmailRead]);
        var evaluator = new JobAuthorizationRuleEvaluator();
        var rules = new IJobAuthorizationRule[]
        {
            new TestRule(
                name: "deny-pass",
                kind: JobAuthorizationRuleKind.Deny,
                result: JobAuthorizationResult.Skip(),
                callOrder),
            new TestRule(
                name: "grant-skip-1",
                kind: JobAuthorizationRuleKind.Grant,
                result: JobAuthorizationResult.Skip(),
                callOrder),
            new TestRule(
                name: "grant-skip-2",
                kind: JobAuthorizationRuleKind.Grant,
                result: JobAuthorizationResult.Skip(),
                callOrder)
        };

        var result = await evaluator.EvaluateAsync(actor, rules, CancellationToken.None);

        Assert.False(result.IsAuthorized);
        Assert.Equal(JobAuthorizationDecision.Deny, result.Decision);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(["deny-pass", "grant-skip-1", "grant-skip-2"], callOrder);
    }

    private sealed class TestRule : IJobAuthorizationRule
    {
        private readonly string _name;
        private readonly JobAuthorizationResult _result;
        private readonly IList<string> _callOrder;

        public TestRule(
            string name,
            JobAuthorizationRuleKind kind,
            JobAuthorizationResult result,
            IList<string> callOrder)
        {
            _name = name;
            Kind = kind;
            _result = result;
            _callOrder = callOrder;
        }

        public JobAuthorizationRuleKind Kind { get; }

        public ValueTask<JobAuthorizationResult> EvaluateAsync(
            JobActor actor,
            CancellationToken cancellationToken)
        {
            _callOrder.Add(_name);
            return ValueTask.FromResult(_result);
        }
    }
}
