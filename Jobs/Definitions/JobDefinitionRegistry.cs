namespace JobSchedulerPrototype.Jobs;

public sealed class JobDefinitionRegistry : IJobDefinitionRegistry
{
    private readonly IReadOnlyDictionary<string, IJobDefinition> _definitions;

    public JobDefinitionRegistry(IEnumerable<IJobDefinition> definitions)
    {
        _definitions = definitions.ToDictionary(
            definition => definition.Type,
            StringComparer.Ordinal);
    }

    public IJobDefinition? Find(string type)
    {
        return _definitions.GetValueOrDefault(type);
    }
}
