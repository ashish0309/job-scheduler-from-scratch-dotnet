namespace JobSchedulerPrototype.Jobs;

public interface IActorOwned
{
    string CreatedByActorId { get; }
}
