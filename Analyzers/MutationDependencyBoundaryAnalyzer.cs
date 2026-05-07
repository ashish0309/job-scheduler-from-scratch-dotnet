using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace JobSchedulerPrototype.ArchitectureAnalyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MutationDependencyBoundaryAnalyzer : DiagnosticAnalyzer
{
    public const string MutationDependencyDiagnosticId = "JSP0001";
    public const string ServiceLocatorDiagnosticId = "JSP0002";

    private static readonly DiagnosticDescriptor MutationDependencyRule = new(
        MutationDependencyDiagnosticId,
        title: "Mutation dependency is outside the allowed layer",
        messageFormat: "Type '{0}' cannot depend on mutation service '{1}'; route through action handlers instead",
        category: "Architecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Prevents direct mutation dependencies outside the allowed architectural layers.");

    private static readonly DiagnosticDescriptor ServiceLocatorRule = new(
        ServiceLocatorDiagnosticId,
        title: "Service locator access is outside the allowed layer",
        messageFormat: "Type '{0}' cannot use service locator access via '{1}'",
        category: "Architecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Prevents container access outside composition root and designated dispatcher types.");

    private static readonly ImmutableHashSet<string> ForbiddenMutationTypeNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "JobSchedulerPrototype.Jobs.IJobStore",
            "JobSchedulerPrototype.Jobs.IJobLifecycleService",
            "JobSchedulerPrototype.Jobs.JobSchedulerDbContext");

    private static readonly ImmutableHashSet<string> ForbiddenServiceLocatorTypeNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "System.IServiceProvider",
            "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [MutationDependencyRule, ServiceLocatorRule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
        context.RegisterOperationAction(AnalyzeInvocationOperation, OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzePropertyReferenceOperation, OperationKind.PropertyReference);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol type
            || type.TypeKind != TypeKind.Class
            || type.IsAbstract
            || type.Locations.Length == 0)
        {
            return;
        }

        var mutationAllowed = IsMutationAllowedType(type);
        var serviceLocatorAllowed = IsServiceLocatorAllowedType(type);
        if (mutationAllowed && serviceLocatorAllowed)
        {
            return;
        }

        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.IsImplicitlyDeclared)
            {
                continue;
            }

            foreach (var parameter in constructor.Parameters)
            {
                if (!mutationAllowed && IsForbiddenMutationType(parameter.Type))
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            MutationDependencyRule,
                            parameter.Locations.FirstOrDefault() ?? constructor.Locations.FirstOrDefault(),
                            type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                            parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                }

                if (!serviceLocatorAllowed && IsForbiddenServiceLocatorType(parameter.Type))
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            ServiceLocatorRule,
                            parameter.Locations.FirstOrDefault() ?? constructor.Locations.FirstOrDefault(),
                            type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                            parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                }
            }
        }
    }

    private static void AnalyzeInvocationOperation(OperationAnalysisContext context)
    {
        if (context.Operation is not IInvocationOperation invocation
            || !IsServiceLocatorInvocation(invocation))
        {
            return;
        }

        var containingType = context.ContainingSymbol.ContainingType;
        if (containingType is null || IsServiceLocatorAllowedType(containingType))
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                ServiceLocatorRule,
                invocation.Syntax.GetLocation(),
                containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                invocation.TargetMethod.Name));
    }

    private static void AnalyzePropertyReferenceOperation(OperationAnalysisContext context)
    {
        if (context.Operation is not IPropertyReferenceOperation propertyReference
            || propertyReference.Property.Name != "RequestServices"
            || NormalizeTypeName(propertyReference.Property.ContainingType) != "Microsoft.AspNetCore.Http.HttpContext")
        {
            return;
        }

        var containingType = context.ContainingSymbol.ContainingType;
        if (containingType is null || IsServiceLocatorAllowedType(containingType))
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                ServiceLocatorRule,
                propertyReference.Syntax.GetLocation(),
                containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                "HttpContext.RequestServices"));
    }

    private static bool IsForbiddenMutationType(ITypeSymbol type)
    {
        var typeName = NormalizeTypeName(type);
        if (ForbiddenMutationTypeNames.Contains(typeName))
        {
            return true;
        }

        if (type is INamedTypeSymbol namedType
            && namedType.IsGenericType
            && NormalizeTypeName(namedType.OriginalDefinition)
            == "Microsoft.EntityFrameworkCore.IDbContextFactory<TContext>")
        {
            return NormalizeTypeName(namedType.TypeArguments[0])
                == "JobSchedulerPrototype.Jobs.JobSchedulerDbContext";
        }

        return false;
    }

    private static bool IsForbiddenServiceLocatorType(ITypeSymbol type)
    {
        return ForbiddenServiceLocatorTypeNames.Contains(NormalizeTypeName(type));
    }

    private static bool IsServiceLocatorInvocation(IInvocationOperation invocation)
    {
        var containingTypeName = NormalizeTypeName(invocation.TargetMethod.ContainingType);
        var methodName = invocation.TargetMethod.Name;

        if ((methodName == "GetRequiredService" || methodName == "GetService" || methodName == "CreateScope")
            && containingTypeName == "Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions")
        {
            return true;
        }

        return methodName == "GetService"
               && containingTypeName == "System.IServiceProvider";
    }

    private static bool IsMutationAllowedType(INamedTypeSymbol type)
    {
        if (Implements(type, "JobSchedulerPrototype.Jobs.IJobStore")
            || Implements(type, "JobSchedulerPrototype.Jobs.IJobLifecycleService"))
        {
            return true;
        }

        return type.AllInterfaces.Any(interfaceSymbol =>
            interfaceSymbol.IsGenericType
            && interfaceSymbol.OriginalDefinition.Name == "IJobActionHandler"
            && interfaceSymbol.OriginalDefinition.ContainingNamespace.ToDisplayString() == "JobSchedulerPrototype.Jobs");
    }

    private static bool IsServiceLocatorAllowedType(INamedTypeSymbol type)
    {
        return IsProgramType(type)
               || Implements(type, "JobSchedulerPrototype.Jobs.IJobActionDispatcher");
    }

    private static bool IsProgramType(INamedTypeSymbol type)
    {
        return type.Name == "Program"
               && type.ContainingNamespace.IsGlobalNamespace;
    }

    private static bool Implements(INamedTypeSymbol type, string interfaceName)
    {
        return type.AllInterfaces.Any(interfaceSymbol =>
            NormalizeTypeName(interfaceSymbol) == interfaceName);
    }

    private static string NormalizeTypeName(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty);
    }
}
