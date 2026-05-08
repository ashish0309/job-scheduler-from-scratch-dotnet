namespace JobSchedulerPrototype.Jobs;

public sealed class DevelopmentActorOptions
{
    public const string SectionName = "JobScheduler:DevelopmentActor";

    public bool AllowRequestHeaders { get; set; } = true;

    public string ActorId { get; set; } = DevelopmentHeaderJobActorProvider.DefaultActorId;

    public string TenantId { get; set; } = DevelopmentHeaderJobActorProvider.DefaultTenantId;

    public string[] Permissions { get; set; } = [];
}
