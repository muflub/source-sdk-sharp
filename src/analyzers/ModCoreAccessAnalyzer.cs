using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SourceSharp.Analyzers;

/// <summary>
/// A MOD REACHES THE CORE ONLY BY CONSTRUCTOR INJECTION, AND EVENTS REACH IT
/// ONLY AS HOOK INTERFACES (plan_host_sdk §2.8, §2.11; the user's rulings of
/// 2026-09-30). The rule set CoreAccessTests applied to the mods' IL, applied
/// to their source:
/// <list type="bullet">
/// <item>SS0010: no static field other than a constant (an auto-property's
/// backing field is the compiler's, as it was to the IL scan), except the two
/// tables ScriptedVarsGenerator writes into a [ScriptVars] class;</item>
/// <item>SS0011: no event declared;</item>
/// <item>SS0012: no use of SourceSharp.Abi.Engine or EngineServices, nor of
/// the process's environment, Exit or FailFast;</item>
/// <item>SS0013: no event subscribed to, except in the one class that adapts
/// the SDK's events (TF2.Descent.Server.HostLink).</item>
/// </list>
/// Operations, not syntax, so a use inside a lambda or a local function is
/// charged to the class that wrote it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ModCoreAccessAnalyzer : DiagnosticAnalyzer
{
    public const string StaticFieldId = "SS0010";
    public const string DeclaresEventId = "SS0011";
    public const string ForbiddenMemberId = "SS0012";
    public const string SubscribesId = "SS0013";

    /// <summary>The one class that may subscribe to C# events: it adapts the SDK's.</summary>
    public const string EventAdapter = "TF2.Descent.Server.HostLink";

    private const string Rule = "a mod reaches the core only by constructor injection and hook interfaces";

    public static readonly DiagnosticDescriptor StaticField = new(
        StaticFieldId, "A mod declares a static field",
        "'{0}' is a static field in a mod: " + Rule + "; keep state in an instance the mod is handed",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DeclaresEvent = new(
        DeclaresEventId, "A mod declares an event",
        "'{0}' declares an event in a mod: " + Rule + "; expose a hook interface instead",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ForbiddenMember = new(
        ForbiddenMemberId, "A mod uses a static of the core or the process",
        "'{0}' uses {1}: " + Rule + "; take what it needs through the constructor",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Subscribes = new(
        SubscribesId, "A mod subscribes to an event",
        "'{0}' subscribes to {1}: " + Rule + "; only " + EventAdapter + " adapts events",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(StaticField, DeclaresEvent, ForbiddenMember, Subscribes);

    /// <summary>Statics of the core and the process a mod must not touch: (type, member or null for all of it).</summary>
    private static readonly (string Type, string? Member)[] Forbidden =
    {
        ("SourceSharp.Abi.Engine", null),
        ("SourceSharp.Abi.EngineServices", null),
        ("System.Environment", "GetEnvironmentVariable"),
        ("System.Environment", "GetEnvironmentVariables"),
        ("System.Environment", "SetEnvironmentVariable"),
        ("System.Environment", "Exit"),
        ("System.Environment", "FailFast"),
    };

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterCompilationStartAction(start =>
        {
            if (!AnalyzerRoles.Has(start.Options, AnalyzerRoles.Mod))
                return;

            start.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
            start.RegisterOperationAction(AnalyzeUse,
                OperationKind.Invocation, OperationKind.FieldReference, OperationKind.PropertyReference,
                OperationKind.EventReference, OperationKind.MethodReference, OperationKind.ObjectCreation);
            start.RegisterOperationAction(AnalyzeSubscription, OperationKind.EventAssignment);
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext ctx)
    {
        var type = (INamedTypeSymbol)ctx.Symbol;

        if (Symbols.IsCompilerGenerated(type))
            return;

        string owner = Symbols.ClrFullName(type);

        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IFieldSymbol field when field.IsStatic && !field.IsConst && !field.IsImplicitlyDeclared
                                             && !Symbols.HasAttribute(field, "System.Runtime.CompilerServices.CompilerGeneratedAttribute")
                                             && !IsGeneratedVarsTable(type, field):
                    ctx.ReportDiagnostic(Diagnostic.Create(StaticField, Symbols.LocationOf(field), owner + "." + field.Name));
                    break;

                case IEventSymbol ev when !ev.IsImplicitlyDeclared:
                    ctx.ReportDiagnostic(Diagnostic.Create(DeclaresEvent, Symbols.LocationOf(ev), owner + "." + ev.Name));
                    break;
            }
        }
    }

    /// <summary>
    /// The two read-only tables the core's ScriptedVarsGenerator writes into
    /// every [ScriptVars] class (its key descriptors and its defaults). They are
    /// generated metadata, not state; nothing else is exempt.
    /// </summary>
    private static bool IsGeneratedVarsTable(INamedTypeSymbol type, IFieldSymbol field) =>
        field.IsReadOnly
        && field.Name is "s_Descriptors" or "s_Defaults"
        && type.GetAttributes().Any(a => a.AttributeClass?.Name == "ScriptVarsAttribute");

    private static void AnalyzeUse(OperationAnalysisContext ctx)
    {
        ISymbol? used = ctx.Operation switch
        {
            IInvocationOperation i => i.TargetMethod,
            IFieldReferenceOperation f => f.Field,
            IPropertyReferenceOperation p => p.Property,
            IEventReferenceOperation e => e.Event,
            IMethodReferenceOperation m => m.Method,
            IObjectCreationOperation o => o.Constructor,
            _ => null,
        };

        if (used?.ContainingType is not { } declaring)
            return;

        // nameof(Engine.X) names a member; it emits no use of it.
        for (IOperation? at = ctx.Operation.Parent; at is not null; at = at.Parent)
        {
            if (at is INameOfOperation)
                return;
        }

        string declaringName = Symbols.ClrFullName(declaring);

        foreach (var (type, member) in Forbidden)
        {
            if (declaringName == type && (member is null || used.Name == member))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(ForbiddenMember, ctx.Operation.Syntax.GetLocation(),
                    Owner(ctx), type + "." + used.Name));
                return;
            }
        }
    }

    private static void AnalyzeSubscription(OperationAnalysisContext ctx)
    {
        var assignment = (IEventAssignmentOperation)ctx.Operation;

        if (!assignment.Adds || assignment.EventReference is not IEventReferenceOperation { Event: var ev })
            return;

        string owner = Owner(ctx);

        if (owner == EventAdapter)
            return;

        ctx.ReportDiagnostic(Diagnostic.Create(Subscribes, assignment.Syntax.GetLocation(),
            owner, Symbols.ClrFullName(ev.ContainingType) + "." + ev.Name));
    }

    /// <summary>The class the code belongs to: a lambda's or a local function's is the class that wrote it.</summary>
    private static string Owner(OperationAnalysisContext ctx) =>
        ctx.ContainingSymbol.ContainingType is { } t ? Symbols.ClrFullName(t) : ctx.ContainingSymbol.Name;
}
