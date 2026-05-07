using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobSchedulerPrototype.Api;

public static class JobApiServiceCollectionExtensions
{
    public static IServiceCollection AddJobApiEndpoints(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        var targetAssemblies = assemblies.Length == 0
            ? [typeof(JobApiServiceCollectionExtensions).Assembly]
            : assemblies;

        var endpointDefinitionTypes = targetAssemblies
            .Distinct()
            .SelectMany(static assembly => assembly.GetTypes())
            .Where(static type =>
                type is { IsAbstract: false, IsInterface: false } &&
                typeof(IJobEndpointDefinition).IsAssignableFrom(type))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal);

        foreach (var endpointDefinitionType in endpointDefinitionTypes)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton(
                typeof(IJobEndpointDefinition),
                endpointDefinitionType));
        }

        return services;
    }
}
