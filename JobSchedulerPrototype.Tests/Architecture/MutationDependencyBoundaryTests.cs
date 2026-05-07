using System.Reflection;
using JobSchedulerPrototype.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobSchedulerPrototype.Tests.Architecture;

public sealed class MutationDependencyBoundaryTests
{
    [Fact]
    public void ForbiddenMutationDependenciesStayInsideAllowedLayers()
    {
        var assembly = typeof(Program).Assembly;
        var violations = new List<string>();

        foreach (var type in assembly.GetTypes())
        {
            if (!IsTargetType(type))
            {
                continue;
            }

            var mutationAllowed = IsMutationAllowedType(type);
            var serviceLocatorAllowed = IsServiceLocatorAllowedType(type);

            foreach (var constructor in type.GetConstructors(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    if (!mutationAllowed && IsForbiddenMutation(parameter.ParameterType))
                    {
                        violations.Add(
                            $"{type.FullName} -> ctor parameter '{parameter.Name}' ({parameter.ParameterType.FullName})");
                    }

                    if (!serviceLocatorAllowed && IsForbiddenServiceLocator(parameter.ParameterType))
                    {
                        violations.Add(
                            $"{type.FullName} -> ctor parameter '{parameter.Name}' ({parameter.ParameterType.FullName})");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Forbidden mutation dependencies found outside allowed layers:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void PagesAndWorkersDoNotDependOnStoreOrLifecycleDirectly()
    {
        Assert.DoesNotContain(
            typeof(global::JobSchedulerPrototype.Pages.IndexModel)
                .GetConstructors()
                .SelectMany(constructor => constructor.GetParameters()),
            parameter => IsForbiddenMutation(parameter.ParameterType)
                || IsForbiddenServiceLocator(parameter.ParameterType));
        Assert.DoesNotContain(
            typeof(global::JobSchedulerPrototype.Pages.Jobs.DetailsModel)
                .GetConstructors()
                .SelectMany(constructor => constructor.GetParameters()),
            parameter => IsForbiddenMutation(parameter.ParameterType)
                || IsForbiddenServiceLocator(parameter.ParameterType));
        Assert.DoesNotContain(
            typeof(QueuedJobWorker).GetConstructors().SelectMany(constructor => constructor.GetParameters()),
            parameter => IsForbiddenMutation(parameter.ParameterType)
                || IsForbiddenServiceLocator(parameter.ParameterType));
    }

    [Fact]
    public void ServiceLocatorApisAppearOnlyInAllowedFiles()
    {
        var repositoryRoot = FindRepositoryRoot();
        var codeFiles = EnumerateApplicationCodeFiles(repositoryRoot).ToArray();

        var violations = new List<string>();
        foreach (var file in codeFiles)
        {
            var normalized = file.Replace('\\', '/');
            var isAllowedFile = normalized.EndsWith("/Program.cs", StringComparison.Ordinal)
                || normalized.EndsWith("/Jobs/Actions/JobActionDispatcher.cs", StringComparison.Ordinal);
            if (isAllowedFile)
            {
                continue;
            }

            var text = File.ReadAllText(file);
            if (text.Contains("GetRequiredService(", StringComparison.Ordinal)
                || text.Contains("GetService(", StringComparison.Ordinal)
                || text.Contains("RequestServices", StringComparison.Ordinal)
                || text.Contains("CreateScope(", StringComparison.Ordinal))
            {
                violations.Add(normalized);
            }
        }

        Assert.True(
            violations.Count == 0,
            "Service locator APIs were used outside allowed files:\n" + string.Join('\n', violations));
    }

    private static bool IsTargetType(Type type)
    {
        return type is { IsClass: true, IsAbstract: false }
               && (!IsMutationAllowedType(type) || !IsServiceLocatorAllowedType(type));
    }

    private static bool IsForbiddenMutation(Type parameterType)
    {
        if (parameterType == typeof(IJobStore)
            || parameterType == typeof(IJobLifecycleService)
            || parameterType == typeof(JobSchedulerDbContext))
        {
            return true;
        }

        return parameterType.IsGenericType
               && parameterType.GetGenericTypeDefinition() == typeof(IDbContextFactory<>)
               && parameterType.GetGenericArguments()[0] == typeof(JobSchedulerDbContext);
    }

    private static bool IsForbiddenServiceLocator(Type parameterType)
    {
        return parameterType == typeof(IServiceProvider)
               || parameterType == typeof(IServiceScopeFactory);
    }

    private static bool IsMutationAllowedType(Type type)
    {
        if (typeof(IJobStore).IsAssignableFrom(type)
            || typeof(IJobLifecycleService).IsAssignableFrom(type))
        {
            return true;
        }

        return type.GetInterfaces().Any(interfaceType =>
            interfaceType.IsGenericType
            && interfaceType.GetGenericTypeDefinition() == typeof(IJobActionHandler<,>));
    }

    private static bool IsServiceLocatorAllowedType(Type type)
    {
        if (type == typeof(Program))
        {
            return true;
        }

        return typeof(IJobActionDispatcher).IsAssignableFrom(type);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "JobSchedulerPrototype.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private static IEnumerable<string> EnumerateApplicationCodeFiles(string repositoryRoot)
    {
        var roots = new[]
        {
            Path.Combine(repositoryRoot, "Api"),
            Path.Combine(repositoryRoot, "Jobs"),
            Path.Combine(repositoryRoot, "Pages")
        };

        foreach (var root in roots)
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');
                if (normalized.Contains("/bin/", StringComparison.Ordinal)
                    || normalized.Contains("/obj/", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return file;
            }
        }

        yield return Path.Combine(repositoryRoot, "Program.cs");
    }
}
