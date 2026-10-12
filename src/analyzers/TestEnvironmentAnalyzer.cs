using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SourceSharp.Analyzers;

/// <summary>
/// SS0020: NO TEST MOVES A PROCESS ENVIRONMENT VARIABLE. The variables are
/// process-global, so a fact that set one shared it with every concurrent
/// test (DebugApiTests set DEBUG_API_DIR and SOURCESHARP_MAPS,
/// CMakeGraphFixtureTests SOURCESHARP_BUILD_DIR); the code under test reads the
/// environment once where it is composed, and a fact hands it its own values.
/// A child process's environment (ProcessStartInfo) is not the process's and
/// is not matched. Applied to the test projects (role "test").
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TestEnvironmentAnalyzer : DiagnosticAnalyzer
{
    public const string SetsEnvironmentId = "SS0020";

    public static readonly DiagnosticDescriptor SetsEnvironment = new(
        SetsEnvironmentId, "A test sets a process environment variable",
        "a test sets a process environment variable, which every concurrent test reads; inject the value instead",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(SetsEnvironment);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterCompilationStartAction(start =>
        {
            if (!AnalyzerRoles.Has(start.Options, AnalyzerRoles.Test))
                return;

            start.RegisterOperationAction(ctx =>
            {
                IMethodSymbol? method = ctx.Operation switch
                {
                    IInvocationOperation i => i.TargetMethod,
                    IMethodReferenceOperation m => m.Method,
                    _ => null,
                };

                if (method is { Name: "SetEnvironmentVariable" } && Symbols.ClrFullName(method.ContainingType) == "System.Environment")
                    ctx.ReportDiagnostic(Diagnostic.Create(SetsEnvironment, ctx.Operation.Syntax.GetLocation()));
            }, OperationKind.Invocation, OperationKind.MethodReference);
        });
    }
}
