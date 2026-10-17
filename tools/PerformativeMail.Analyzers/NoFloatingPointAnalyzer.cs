using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace PerformativeMail.Analyzers;

/// <summary>
/// ADR-0006 section 3, determinism rule 1 (TR-sim-031): the authoritative Sim uses
/// integer numerics only. Reports <c>PMSIM001</c> (Error) for every operation or
/// declaration whose type is <c>System.Single</c>, <c>System.Double</c>,
/// <c>System.Decimal</c> or <c>System.Half</c>, or is an array, pointer, nullable or
/// generic construction that contains one (<c>double[]</c>, <c>double?</c>,
/// <c>List&lt;float&gt;</c>).
/// <para>
/// Detection is type-based (<see cref="SpecialType"/>), not syntax-based, so
/// <c>var x = a * 1.5;</c> and an implicit int-to-double conversion are caught as well
/// as the <c>double</c> keyword. Compiler-synthesised members (record members) have no
/// syntax and are not visited. Not thread-affine: all state is immutable after
/// compilation start, so concurrent execution is enabled.
/// </para>
/// </summary>
/// <example>
/// <code>
/// double d = 0;          // PMSIM001 (local declaration)
/// var x = a * 1.5;       // PMSIM001 (literal, binary operation)
/// float f;               // PMSIM001 (field/local declaration)
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoFloatingPointAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Diagnostic id reported for every floating-point or decimal use.</summary>
    public const string DiagnosticId = "PMSIM001";

    private static readonly DiagnosticDescriptor _rule = new(
        id: DiagnosticId,
        title: "Floating-point or decimal type in authoritative Sim code",
        messageFormat: "{0} uses banned numeric type '{1}': the Sim is integer-only (ADR-0006 determinism rule 1)",
        category: "Determinism",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "float, double, decimal and System.Half are banned in PerformativeMail.Sim. Use Fx16, Cents or integer arithmetic (ADR-0006).");

    private static readonly ImmutableArray<OperationKind> _operationKinds = ImmutableArray.Create(
        OperationKind.Literal,
        OperationKind.Conversion,
        OperationKind.BinaryOperator,
        OperationKind.UnaryOperator,
        OperationKind.Invocation,
        OperationKind.FieldReference,
        OperationKind.PropertyReference,
        OperationKind.LocalReference,
        OperationKind.ParameterReference,
        OperationKind.ObjectCreation,
        OperationKind.ArrayCreation,
        OperationKind.DefaultValue,
        OperationKind.TypeOf,
        OperationKind.SizeOf,
        OperationKind.DeclarationExpression,
        OperationKind.VariableDeclarator,
        OperationKind.DeclarationPattern,
        OperationKind.AnonymousFunction,
        OperationKind.LocalFunction);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(_rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext start)
    {
        // System.Half has no SpecialType; resolve it once per compilation (null on
        // frameworks that predate it, which then simply cannot use it).
        INamedTypeSymbol? half = start.Compilation.GetTypeByMetadataName("System.Half");

        start.RegisterSymbolAction(ctx => AnalyzeNamedType(ctx, half), SymbolKind.NamedType);
        start.RegisterSymbolAction(ctx => AnalyzeMethod(ctx, half), SymbolKind.Method);
        start.RegisterSymbolAction(ctx => AnalyzeField(ctx, half), SymbolKind.Field);
        start.RegisterSymbolAction(ctx => AnalyzeProperty(ctx, half), SymbolKind.Property);
        start.RegisterSymbolAction(ctx => AnalyzeEvent(ctx, half), SymbolKind.Event);
        start.RegisterOperationAction(ctx => AnalyzeOperation(ctx, half), _operationKinds);
    }

    // ---- type test -------------------------------------------------------------

    /// <summary>Returns the offending banned type inside <paramref name="type"/>, or null.</summary>
    private static ITypeSymbol? FindBanned(ITypeSymbol? type, INamedTypeSymbol? half)
    {
        switch (type)
        {
            case null:
                return null;
            case IArrayTypeSymbol array:
                return FindBanned(array.ElementType, half);
            case IPointerTypeSymbol pointer:
                return FindBanned(pointer.PointedAtType, half);
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
                return type;
        }

        if (half is not null && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, half))
        {
            return type;
        }

        if (type is INamedTypeSymbol named)
        {
            foreach (ITypeSymbol argument in named.TypeArguments)
            {
                ITypeSymbol? inner = FindBanned(argument, half);
                if (inner is not null)
                {
                    return inner;
                }
            }
        }

        return null;
    }

    private static void Report(Location location, string what, ITypeSymbol banned, Action<Diagnostic> sink)
    {
        sink(Diagnostic.Create(_rule, location, what, banned.ToDisplayString()));
    }

    private static Location? SourceLocation(ISymbol symbol)
    {
        foreach (Location location in symbol.Locations)
        {
            if (location.IsInSource)
            {
                return location;
            }
        }

        return null;
    }

    // ---- declarations ----------------------------------------------------------

    private static void CheckSymbol(ISymbol symbol, ITypeSymbol? type, string what, INamedTypeSymbol? half, Action<Diagnostic> sink)
    {
        ITypeSymbol? banned = FindBanned(type, half);
        Location? location = SourceLocation(symbol);
        if (banned is not null && location is not null)
        {
            Report(location, what, banned, sink);
        }
    }

    private static void CheckParameters(ImmutableArray<IParameterSymbol> parameters, INamedTypeSymbol? half, Action<Diagnostic> sink)
    {
        foreach (IParameterSymbol parameter in parameters)
        {
            CheckSymbol(parameter, parameter.Type, "Parameter '" + parameter.Name + "'", half, sink);
        }
    }

    private static void AnalyzeField(SymbolAnalysisContext ctx, INamedTypeSymbol? half)
    {
        var field = (IFieldSymbol)ctx.Symbol;
        if (!field.IsImplicitlyDeclared)
        {
            CheckSymbol(field, field.Type, "Field '" + field.Name + "'", half, ctx.ReportDiagnostic);
        }
    }

    private static void AnalyzeProperty(SymbolAnalysisContext ctx, INamedTypeSymbol? half)
    {
        var property = (IPropertySymbol)ctx.Symbol;
        if (property.IsImplicitlyDeclared)
        {
            return;
        }

        CheckSymbol(property, property.Type, "Property '" + property.Name + "'", half, ctx.ReportDiagnostic);
        CheckParameters(property.Parameters, half, ctx.ReportDiagnostic);
    }

    private static void AnalyzeEvent(SymbolAnalysisContext ctx, INamedTypeSymbol? half)
    {
        var evt = (IEventSymbol)ctx.Symbol;
        if (!evt.IsImplicitlyDeclared)
        {
            CheckSymbol(evt, evt.Type, "Event '" + evt.Name + "'", half, ctx.ReportDiagnostic);
        }
    }

    private static void AnalyzeMethod(SymbolAnalysisContext ctx, INamedTypeSymbol? half)
    {
        var method = (IMethodSymbol)ctx.Symbol;
        if (method.IsImplicitlyDeclared)
        {
            return;
        }

        switch (method.MethodKind)
        {
            case MethodKind.PropertyGet:
            case MethodKind.PropertySet:
            case MethodKind.EventAdd:
            case MethodKind.EventRemove:
            case MethodKind.EventRaise:
                return; // the owning property / event is reported instead
        }

        CheckSymbol(method, method.ReturnType, "Return type of '" + method.Name + "'", half, ctx.ReportDiagnostic);
        CheckParameters(method.Parameters, half, ctx.ReportDiagnostic);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext ctx, INamedTypeSymbol? half)
    {
        var type = (INamedTypeSymbol)ctx.Symbol;
        if (type.IsImplicitlyDeclared)
        {
            return;
        }

        // Base type and interface arguments, e.g. IEquatable<double>.
        CheckSymbol(type, type.BaseType, "Base type of '" + type.Name + "'", half, ctx.ReportDiagnostic);
        foreach (INamedTypeSymbol implemented in type.Interfaces)
        {
            CheckSymbol(type, implemented, "Interface list of '" + type.Name + "'", half, ctx.ReportDiagnostic);
        }

        if (type.TypeKind == TypeKind.Delegate && type.DelegateInvokeMethod is { } invoke)
        {
            CheckSymbol(type, invoke.ReturnType, "Delegate '" + type.Name + "' return type", half, ctx.ReportDiagnostic);
            foreach (IParameterSymbol parameter in invoke.Parameters)
            {
                CheckSymbol(type, parameter.Type, "Delegate '" + type.Name + "' parameter '" + parameter.Name + "'", half, ctx.ReportDiagnostic);
            }
        }
    }

    // ---- operations ------------------------------------------------------------

    private static void AnalyzeOperation(OperationAnalysisContext ctx, INamedTypeSymbol? half)
    {
        IOperation operation = ctx.Operation;
        Action<Diagnostic> sink = ctx.ReportDiagnostic;
        Location location = operation.Syntax.GetLocation();

        // Declaration-like and function-like operations report their own types only.
        if (AnalyzeTypeOperand(operation, location, half, sink)
            || AnalyzeDeclaration(operation, location, half, sink)
            || AnalyzeFunction(operation, location, half, sink))
        {
            return;
        }

        if (operation is IInvocationOperation invocation)
        {
            AnalyzeInvocationTypeArguments(invocation, location, half, sink);
        }

        ReportIfBanned(operation.Type, "Expression", location, half, sink);
    }

    /// <summary><c>typeof(T)</c> and <c>sizeof(T)</c>. Returns true when handled.</summary>
    private static bool AnalyzeTypeOperand(IOperation operation, Location location, INamedTypeSymbol? half, Action<Diagnostic> sink)
    {
        switch (operation)
        {
            case ITypeOfOperation typeOf:
                ReportIfBanned(typeOf.TypeOperand, "typeof", location, half, sink);
                return true;
            case ISizeOfOperation sizeOf:
                ReportIfBanned(sizeOf.TypeOperand, "sizeof", location, half, sink);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Variable declarators and declaration patterns. Returns true when handled.</summary>
    private static bool AnalyzeDeclaration(IOperation operation, Location location, INamedTypeSymbol? half, Action<Diagnostic> sink)
    {
        switch (operation)
        {
            case IVariableDeclaratorOperation declarator:
                ReportIfBanned(declarator.Symbol.Type, "Variable '" + declarator.Symbol.Name + "'", location, half, sink);
                return true;
            case IDeclarationPatternOperation pattern:
                ReportIfBanned(pattern.NarrowedType, "Declaration pattern", location, half, sink);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Lambdas and local functions. Returns true when handled.</summary>
    private static bool AnalyzeFunction(IOperation operation, Location location, INamedTypeSymbol? half, Action<Diagnostic> sink)
    {
        switch (operation)
        {
            case IAnonymousFunctionOperation lambda:
                ReportIfBanned(lambda.Symbol.ReturnType, "Lambda return type", location, half, sink);
                foreach (IParameterSymbol parameter in lambda.Symbol.Parameters)
                {
                    ReportIfBanned(parameter.Type, "Lambda parameter '" + parameter.Name + "'", location, half, sink);
                }

                return true;
            case ILocalFunctionOperation localFunction:
                ReportIfBanned(localFunction.Symbol.ReturnType, "Local function '" + localFunction.Symbol.Name + "' return type", location, half, sink);
                foreach (IParameterSymbol parameter in localFunction.Symbol.Parameters)
                {
                    ReportIfBanned(parameter.Type, "Parameter '" + parameter.Name + "'", location, half, sink);
                }

                return true;
            default:
                return false;
        }
    }

    private static void AnalyzeInvocationTypeArguments(IInvocationOperation invocation, Location location, INamedTypeSymbol? half, Action<Diagnostic> sink)
    {
        foreach (ITypeSymbol argument in invocation.TargetMethod.TypeArguments)
        {
            ReportIfBanned(argument, "Generic call '" + invocation.TargetMethod.Name + "'", location, half, sink);
        }
    }

    private static void ReportIfBanned(ITypeSymbol? type, string what, Location location, INamedTypeSymbol? half, Action<Diagnostic> sink)
    {
        ITypeSymbol? banned = FindBanned(type, half);
        if (banned is not null)
        {
            Report(location, what, banned, sink);
        }
    }
}
