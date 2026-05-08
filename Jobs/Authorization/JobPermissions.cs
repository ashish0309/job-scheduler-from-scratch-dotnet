namespace JobSchedulerPrototype.Jobs;

public static class JobPermissions
{
    public const string All = "jobs.*";

    public const string GlobalRead = "jobs.global.read";

    public const string EmailRead = "jobs.email.read";

    public const string EmailEnqueue = "jobs.email.enqueue";

    public const string EmailManage = "jobs.email.manage";

    public const string Execute = "jobs.execute";
}
