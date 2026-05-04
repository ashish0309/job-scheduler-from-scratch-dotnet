namespace JobSchedulerPrototype.Jobs;

public sealed class JobHandlerRegistry : IJobHandlerRegistry
{
    private readonly IReadOnlyDictionary<string, IJobHandler> _handlers;

    public JobHandlerRegistry(IEnumerable<IJobHandler> handlers)
    {
        _handlers = handlers.ToDictionary(
            handler => handler.Type,
            StringComparer.Ordinal);
    }

    public IJobHandler? Find(string type)
    {
        return _handlers.GetValueOrDefault(type);
    }
}
