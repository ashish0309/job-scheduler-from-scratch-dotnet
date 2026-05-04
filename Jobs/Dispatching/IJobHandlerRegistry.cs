namespace JobSchedulerPrototype.Jobs;

public interface IJobHandlerRegistry
{
    IJobHandler? Find(string type);
}
