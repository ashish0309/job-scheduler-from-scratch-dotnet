namespace JobSchedulerPrototype.Jobs;

public sealed class WorkerActorOptions
{
    public const string SectionName = "JobScheduler:WorkerActor";

    public string ActorId { get; set; } = "worker-service";

    public string TenantId { get; set; } = "system";

    public string[] Permissions { get; set; } = [JobPermissions.Execute];

    public JobActor ToActor()
    {
        return new JobActor(
            string.IsNullOrWhiteSpace(ActorId) ? "worker-service" : ActorId.Trim(),
            string.IsNullOrWhiteSpace(TenantId) ? "system" : TenantId.Trim(),
            Permissions is { Length: > 0 } ? Permissions : [JobPermissions.Execute]);
    }
}
