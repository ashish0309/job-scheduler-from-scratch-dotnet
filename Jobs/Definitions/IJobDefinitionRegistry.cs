namespace JobSchedulerPrototype.Jobs;

public interface IJobDefinitionRegistry
{
    IJobDefinition? Find(string type);
}
