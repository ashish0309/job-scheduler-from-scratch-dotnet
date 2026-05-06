namespace JobSchedulerPrototype.Jobs;

public interface ITenantScoped
{
    string TenantId { get; }
}
