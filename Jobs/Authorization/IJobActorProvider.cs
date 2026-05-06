namespace JobSchedulerPrototype.Jobs;

public interface IJobActorProvider
{
    JobActor GetCurrentActor();
}
