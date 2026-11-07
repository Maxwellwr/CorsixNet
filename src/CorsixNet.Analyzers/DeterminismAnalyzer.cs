using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CorsixNet.Analyzers;

/// <summary>
/// Reports constructs that would make the lockstep simulation diverge
/// between machines: wall-clock time, unseeded randomness, concurrency,
/// floating-point arithmetic and iteration over unordered collections.
/// </summary>
/// <remarks>
/// Active only in projects that set the MSBuild property
/// <c>CorsixNetDeterministic</c> to <c>true</c>.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeterminismAnalyzer : DiagnosticAnalyzer
{
    public const string BannedApiId = "CNET0001";
    public const string FloatingPointId = "CNET0002";
    public const string UnorderedIterationId = "CNET0003";

    internal const string EnableOption = "build_property.CorsixNetDeterministic";

    private const string Category = "Determinism";

    internal static readonly DiagnosticDescriptor BannedApi = new(
        BannedApiId,
        title: "Non-deterministic API in simulation code",
        messageFormat: "'{0}' is not allowed in deterministic code: {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Simulation code must produce identical results on every machine in a lockstep game.");

    internal static readonly DiagnosticDescriptor FloatingPoint = new(
        FloatingPointId,
        title: "Floating-point type in simulation code",
        messageFormat: "Floating-point type '{0}' is not allowed in deterministic code; use integers or a fixed-point type",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Floating-point results may differ between CPUs, JIT versions and compiler optimisations.");

    internal static readonly DiagnosticDescriptor UnorderedIteration = new(
        UnorderedIterationId,
        title: "Iteration over an unordered collection in simulation code",
        messageFormat: "Iterating '{0}' has no defined order; iterate an ordered collection or sort the elements first",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Hash-based collections enumerate in an order that depends on hash codes and on the history of insertions and removals.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(BannedApi, FloatingPoint, UnorderedIteration);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var globalOptions = context.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
        if (!globalOptions.TryGetValue(EnableOption, out var enabled)
            || !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var symbols = new KnownSymbols(context.Compilation);

        context.RegisterSymbolAction(c => AnalyzeField(c, symbols), SymbolKind.Field);
        context.RegisterSymbolAction(c => AnalyzeProperty(c, symbols), SymbolKind.Property);
        context.RegisterSymbolAction(c => AnalyzeMethod(c, symbols), SymbolKind.Method);

        context.RegisterOperationAction(c => AnalyzeVariableDeclarator(c, symbols), OperationKind.VariableDeclarator);
        context.RegisterOperationAction(c => AnalyzeObjectCreation(c, symbols), OperationKind.ObjectCreation);
        context.RegisterOperationAction(c => AnalyzeInvocation(c, symbols), OperationKind.Invocation);
        context.RegisterOperationAction(
            c => AnalyzeMemberReference(c, symbols),
            OperationKind.PropertyReference,
            OperationKind.FieldReference,
            OperationKind.EventReference,
            OperationKind.MethodReference);
        context.RegisterOperationAction(c => AnalyzeLiteral(c), OperationKind.Literal);
        context.RegisterOperationAction(c => AnalyzeConversion(c, symbols), OperationKind.Conversion);
        context.RegisterOperationAction(c => AnalyzeForEach(c, symbols), OperationKind.Loop);
    }

    private static void AnalyzeField(SymbolAnalysisContext context, KnownSymbols symbols)
    {
        var field = (IFieldSymbol)context.Symbol;
        if (!field.IsImplicitlyDeclared)
        {
            ReportDeclaredType(context.ReportDiagnostic, symbols, field.Type, field.Locations[0]);
        }
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context, KnownSymbols symbols)
    {
        var property = (IPropertySymbol)context.Symbol;
        if (property.IsImplicitlyDeclared)
        {
            return;
        }

        ReportDeclaredType(context.ReportDiagnostic, symbols, property.Type, property.Locations[0]);
        foreach (var parameter in property.Parameters)
        {
            ReportDeclaredType(context.ReportDiagnostic, symbols, parameter.Type, parameter.Locations[0]);
        }
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context, KnownSymbols symbols)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method.IsImplicitlyDeclared || method.AssociatedSymbol is not null)
        {
            // Accessors are covered by their property or event.
            return;
        }

        if (!method.ReturnsVoid)
        {
            ReportDeclaredType(context.ReportDiagnostic, symbols, method.ReturnType, method.Locations[0]);
        }

        foreach (var parameter in method.Parameters)
        {
            if (!parameter.IsImplicitlyDeclared && parameter.Locations.Length > 0)
            {
                ReportDeclaredType(context.ReportDiagnostic, symbols, parameter.Type, parameter.Locations[0]);
            }
        }
    }

    private static void AnalyzeVariableDeclarator(OperationAnalysisContext context, KnownSymbols symbols)
    {
        var declarator = (IVariableDeclaratorOperation)context.Operation;
        ReportDeclaredType(context.ReportDiagnostic, symbols, declarator.Symbol.Type, declarator.Syntax.GetLocation());
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context, KnownSymbols symbols)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (IsReportedByDeclaration(creation))
        {
            // The declaration that owns the initializer is reported instead.
            return;
        }

        if (creation.Type is { } type && symbols.GetBannedTypeReason(type) is { } reason)
        {
            Report(context.ReportDiagnostic, BannedApi, creation.Syntax.GetLocation(), Display(type), reason);
        }
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, KnownSymbols symbols)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        var location = invocation.Syntax.GetLocation();

        if (HasBannedInstance(invocation.Instance, symbols)
            || ReportBannedMember(context.ReportDiagnostic, symbols, method, location))
        {
            return;
        }

        if (symbols.IsNonDeterministicGetHashCode(method))
        {
            Report(
                context.ReportDiagnostic,
                BannedApi,
                location,
                Display(method),
                "string and identity hash codes are randomised per process");
            return;
        }

        if (!method.ReturnsVoid && ContainsFloatingPoint(method.ReturnType) is { } floatType)
        {
            Report(context.ReportDiagnostic, FloatingPoint, location, Display(floatType));
        }
    }

    private static void AnalyzeMemberReference(OperationAnalysisContext context, KnownSymbols symbols)
    {
        var reference = (IMemberReferenceOperation)context.Operation;
        var member = reference.Member;
        var location = reference.Syntax.GetLocation();

        if (HasBannedInstance(reference.Instance, symbols)
            || ReportBannedMember(context.ReportDiagnostic, symbols, member, location))
        {
            return;
        }

        // Members declared in source are reported at their declaration.
        if (member.Locations.Length > 0 && member.Locations[0].IsInSource)
        {
            return;
        }

        var memberType = member switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => null,
        };
        if (memberType is not null && ContainsFloatingPoint(memberType) is { } floatType)
        {
            Report(context.ReportDiagnostic, FloatingPoint, location, Display(floatType));
        }
    }

    private static void AnalyzeLiteral(OperationAnalysisContext context)
    {
        var literal = context.Operation;
        if (IsReportedByDeclaration(literal))
        {
            // The declaration that owns the initializer is reported instead.
            return;
        }

        if (literal.Type is { } type && IsFloatingPoint(type))
        {
            Report(context.ReportDiagnostic, FloatingPoint, literal.Syntax.GetLocation(), Display(type));
        }
    }

    private static void AnalyzeConversion(OperationAnalysisContext context, KnownSymbols symbols)
    {
        var conversion = (IConversionOperation)context.Operation;

        if (!conversion.IsImplicit && conversion.Type is { } target && IsFloatingPoint(target))
        {
            Report(context.ReportDiagnostic, FloatingPoint, conversion.Syntax.GetLocation(), Display(target));
            return;
        }

        var source = conversion.Operand.Type;
        if (source is null
            || conversion.Parent is IForEachLoopOperation
            || conversion.Type is not { } targetType
            || !symbols.IsUnordered(source)
            || !symbols.IsEnumerableInterface(targetType)
            || IsOrderIndependentLinqArgument(conversion, symbols))
        {
            return;
        }

        Report(context.ReportDiagnostic, UnorderedIteration, conversion.Operand.Syntax.GetLocation(), Display(source));
    }

    private static void AnalyzeForEach(OperationAnalysisContext context, KnownSymbols symbols)
    {
        if (context.Operation is not IForEachLoopOperation loop)
        {
            return;
        }

        var collection = loop.Collection;
        while (collection is IConversionOperation { IsImplicit: true } conversion)
        {
            collection = conversion.Operand;
        }

        if (collection.Type is { } type && symbols.IsUnordered(type))
        {
            Report(context.ReportDiagnostic, UnorderedIteration, collection.Syntax.GetLocation(), Display(type));
        }
    }

    /// <summary>
    /// Whether <paramref name="operation"/> initializes a declaration of the
    /// same type, in which case the declaration's diagnostic already covers it.
    /// </summary>
    private static bool IsReportedByDeclaration(IOperation operation)
    {
        var type = operation.Type;
        var parent = operation.Parent;
        while (parent is IConversionOperation { IsImplicit: true } conversion
            && SymbolEqualityComparer.Default.Equals(conversion.Type, type))
        {
            // Target-typed new() is wrapped in an implicit identity conversion.
            parent = conversion.Parent;
        }

        return parent switch
        {
            IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator } =>
                SymbolEqualityComparer.Default.Equals(declarator.Symbol.Type, type),
            IFieldInitializerOperation initializer =>
                initializer.InitializedFields.Any(f => SymbolEqualityComparer.Default.Equals(f.Type, type)),
            IPropertyInitializerOperation initializer =>
                initializer.InitializedProperties.Any(p => SymbolEqualityComparer.Default.Equals(p.Type, type)),
            IParameterInitializerOperation initializer =>
                SymbolEqualityComparer.Default.Equals(initializer.Parameter.Type, type),
            _ => false,
        };
    }

    private static bool IsOrderIndependentLinqArgument(IConversionOperation conversion, KnownSymbols symbols)
    {
        return conversion.Parent is IArgumentOperation { Parent: IInvocationOperation invocation }
            && symbols.IsOrderIndependentLinqMethod(invocation.TargetMethod);
    }

    /// <summary>
    /// Whether the receiver of a member access is itself of a banned type,
    /// in which case the receiver's declaration or creation is already reported.
    /// </summary>
    private static bool HasBannedInstance(IOperation? instance, KnownSymbols symbols)
    {
        return instance?.Type is { } type && symbols.GetBannedTypeReason(type) is not null;
    }

    private static bool ReportBannedMember(
        Action<Diagnostic> report,
        KnownSymbols symbols,
        ISymbol member,
        Location location)
    {
        var reason = symbols.GetBannedMemberReason(member);
        if (reason is null && member.ContainingType is { } containingType)
        {
            reason = symbols.GetBannedTypeReason(containingType);
        }

        if (reason is null)
        {
            return false;
        }

        Report(report, BannedApi, location, Display(member), reason);
        return true;
    }

    private static void ReportDeclaredType(
        Action<Diagnostic> report,
        KnownSymbols symbols,
        ITypeSymbol type,
        Location location)
    {
        if (symbols.FindBannedType(type) is { } banned)
        {
            Report(report, BannedApi, location, Display(banned.Type), banned.Reason);
        }
        else if (ContainsFloatingPoint(type) is { } floatType)
        {
            Report(report, FloatingPoint, location, Display(floatType));
        }
    }

    private static ITypeSymbol? ContainsFloatingPoint(ITypeSymbol type)
    {
        return KnownSymbols.FindInTypeTree(type, IsFloatingPoint);
    }

    private static bool IsFloatingPoint(ITypeSymbol type)
    {
        return type.SpecialType is SpecialType.System_Single or SpecialType.System_Double
            || type is { Name: "Half", ContainingNamespace.Name: "System", ContainingNamespace.ContainingNamespace.IsGlobalNamespace: true };
    }

    private static string Display(ISymbol symbol)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
    }

    private static void Report(
        Action<Diagnostic> report,
        DiagnosticDescriptor descriptor,
        Location location,
        params object[] args)
    {
        report(Diagnostic.Create(descriptor, location, args));
    }
}
