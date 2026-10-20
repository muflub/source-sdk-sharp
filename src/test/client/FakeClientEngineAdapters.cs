using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Tests;

/// <summary>
/// The engine adapters a test composition of <c>ClientModule</c> registers in
/// place of the real ones: a mock of each seam the composition asks for, whose
/// every member answers its default (zero, false, null, nothing done).
///
/// <para>
/// What such a fact checks is the managed composition (the mirror, the
/// caches, two modules sharing nothing). The real adapters call shim areas,
/// which a test engine never attached and which have nothing to do with what
/// these facts check; each adapter's own slot calls are covered over a
/// <see cref="FakeShimRoot"/> by ClientAdapterRegistrationTests.
/// </para>
/// </summary>
internal sealed class FakeClientEngineAdapters : IClientEngineAdapters
{
    // The seams the composition resolves while it composes: the ones a
    // binding or registry takes by constructor or ClientModule requires.
    private static readonly Type[] Seams =
    [
        typeof(IClientConVarSlots),
        typeof(IClientDebugOverlay),
        typeof(IClientImpactSoundWorld),
        typeof(IClientModelNames),
        typeof(IClientParticleOneShots),
        typeof(IClientPhysicsEngine),
        typeof(IClientPhysicsObjectReads),
        typeof(IClientSplashCarryOut),
        typeof(IFxRender),
        typeof(IFxSpriteDraw),
        typeof(IPixelVisSlots),
        typeof(INativeParticleExecutor),
        typeof(IRenderTargetTable),
    ];

    /// <summary>How many times a composition asked for the adapters.</summary>
    public int Calls { get; private set; }

    /// <summary>The engine the last composition asked over.</summary>
    public ModuleEngine? Engine { get; private set; }

    public void AddTo(ServiceContainer services, ModuleEngine engine)
    {
        Calls++;
        Engine = engine;
        foreach (Type seam in Seams)
            services.AddInstance(seam, DispatchProxy.Create(seam, typeof(DefaultAnswers)));
    }

    /// <summary>A mock whose every member answers its return type's default.</summary>
    public class DefaultAnswers : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Type? returns = targetMethod?.ReturnType;
            return returns is null || returns == typeof(void) || !returns.IsValueType ? null : Activator.CreateInstance(returns);
        }
    }
}
