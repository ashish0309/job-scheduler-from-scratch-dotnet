using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobSchedulerPrototype.Jobs;

public static class JobActionServiceCollectionExtensions
{
    public static IServiceCollection AddJobActions(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        var targetAssemblies = assemblies.Length == 0
            ? [typeof(JobActionServiceCollectionExtensions).Assembly]
            : assemblies;

        services.TryAddSingleton<IJobActionDispatcher, JobActionDispatcher>();

        var handlerTypes = targetAssemblies
            .Distinct()
            .SelectMany(static assembly => assembly.GetTypes())
            .Where(static type => type is { IsAbstract: false, IsInterface: false })
            .OrderBy(static type => type.FullName, StringComparer.Ordinal);

        foreach (var handlerType in handlerTypes)
        {
            var handlerInterfaces = handlerType.GetInterfaces()
                .Where(static interfaceType =>
                    interfaceType.IsGenericType &&
                    interfaceType.GetGenericTypeDefinition() == typeof(IJobActionHandler<,>));

            foreach (var handlerInterface in handlerInterfaces)
            {
                services.TryAddEnumerable(ServiceDescriptor.Singleton(
                    handlerInterface,
                    handlerType));
            }
        }

        return services;
    }
}
