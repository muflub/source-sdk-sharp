namespace SourceSharp.Abi;

//=============================================================================
// IUniformRandomStream, THE FIRST ENGINE INTERFACE CALLED FROM C# WITH NO C++
// IN BETWEEN.
//
// Chosen on evidence rather than on any plan's phase order: docs/plan-audit.md
// §3.12 re-derived the demand over the 288 files the client actually compiles
// and found 873 call sites, the largest of any interface on the boundary and
// 1.6x the next one. Its shape is also the simplest there is -- single
// inheritance, four methods, every parameter and return a primitive -- so what
// it proves is the mechanism and not the exceptions.
//
// NO VIRTUAL DESTRUCTOR, and that is a fact worth knowing rather than assuming:
// the declaration at public/vstdlib/random.h:28 is COMMENTED OUT. Counting it
// would put the Itanium complete/deleting pair above these four and move every
// slot by two.
//=============================================================================

/// <summary>
/// The engine's uniform random stream, as game code should see it — no
/// pointer, no slot number, no <c>unsafe</c>.
/// </summary>
public interface IUniformRandom
{
    // NO SetSeed, AND THAT IS THE WHOLE OF HOW THE DECISION IS EXPRESSED.
    //
    // Managed code must not be a source of server randomness. Server randomness
    // is replayed by demo playback and has to come out identically; a managed
    // caller may ASK for a number and may not decide where the sequence starts.
    // shell_worldquery.cpp makes the same argument about the flat ABI slot.
    //
    // There is no table saying so and no flag on a field. The method is not
    // here, so there is nothing to call. VtableLayouts.g.cs still TYPES slot 0,
    // because the struct describes the engine's ABI and the ABI is what it is
    // whatever anybody decides.

    /// <summary>A float in <c>[min, max]</c>.</summary>
    float RandomFloat(float min, float max);

    /// <summary>An int in <c>[min, max]</c>.</summary>
    int RandomInt(int min, int max);

    /// <summary>An exponentially weighted float.</summary>
    float RandomFloatExp(float min, float max, float exponent);
}

/// <summary>
/// <see cref="IUniformRandom"/> over the engine's own object, called through
/// its vtable.
/// </summary>
public sealed unsafe class UniformRandomStream : IUniformRandom
{
    /// <summary>The engine's factory name for the singleton, in both DLLs.</summary>
    public const string InterfaceVersion = "VEngineRandom001";

    private readonly void* _self;
    private readonly UniformRandomStreamVtbl* _vtbl;

    private UniformRandomStream(void* self, void** table)
    {
        _self = self;
        _vtbl = (UniformRandomStreamVtbl*)table;
    }

    /// <summary>
    /// The engine's stream, or null with the reason.
    ///
    /// <para>
    /// The whole of the acquire is <see cref="Vtable.TryAcquire{TVtbl}"/>:
    /// native answers for the version string and says how long the table is,
    /// and that number has to be this struct's field count. There is nothing
    /// else to check and nothing at all per call.
    /// </para>
    ///
    /// <para>
    /// Answers the INTERFACE rather than this class, so a caller still sees no
    /// pointer and needs no <c>unsafe</c>.
    /// </para>
    ///
    /// <para>
    /// Through <paramref name="engine"/>'s own side's interface factory
    /// (plan_abi_hotreload D8), never the process-wide one that followed the
    /// thread's active side.
    /// </para>
    /// </summary>
    public static IUniformRandom? Acquire(ModuleEngine engine, out string why)
        => Acquire((engine ?? throw new ArgumentNullException(nameof(engine))).Api.Interfaces, out why);

    /// <summary>
    /// The same, through one side's own interface factory (plan_abi_hotreload
    /// D8): what <see cref="EngineBound"/> holds per side.
    /// </summary>
    internal static IUniformRandom? Acquire(NativeInterfaceFactory factory, out string why)
        => Vtable.TryAcquire<UniformRandomStreamVtbl>(
               factory, SourceSharpFactoryType.AppSystem,
               InterfaceVersion, out void* self, out void** table, out why)
            ? new UniformRandomStream(self, table)
            : null;

    /// <inheritdoc/>
    public float RandomFloat(float min, float max) => _vtbl->RandomFloat(_self, min, max);

    /// <inheritdoc/>
    public int RandomInt(int min, int max) => _vtbl->RandomInt(_self, min, max);

    /// <inheritdoc/>
    public float RandomFloatExp(float min, float max, float exponent)
        => _vtbl->RandomFloatExp(_self, min, max, exponent);
}
