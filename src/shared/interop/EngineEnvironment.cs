namespace SourceSharp.Abi;

/// <summary>
/// The environment as the side's engine supplies it: the engine's globals, and
/// the engine's interface factory by name.
///
/// <para>
/// <b>Neither is cached.</b> <see cref="Globals"/> rebuilds its eight-byte
/// reader over the side's pointer each time it is asked, so nothing here holds
/// a copy of anything the engine writes. That is what the "live, not a
/// snapshot" claim rests on, and it is a fact rather than this paragraph.
/// </para>
///
/// <para>
/// <b>An engine AREA, like the other twenty-four, and that is not an accident
/// of the name.</b> EngineServices pairs <c>IEngineFoo</c> with <c>EngineFoo</c>
/// by reflection, so this interface enrols itself; and it should, because the
/// factory and the globals are exactly "a part of the engine a class touches".
/// That means the binding is <c>internal</c> — a class holding
/// <c>EngineEnvironment</c> rather than <see cref="IEngineEnvironment"/> would
/// compile, work, and be untestable — and it means <see cref="RecordingEngine"/>
/// answers for it in tests like it answers for every other area.
/// </para>
///
/// <para>
/// <b>Both halves are the side's own.</b> The globals are the side's live
/// <c>CGlobalVars</c> (plan_crossings.md Phase X1's <c>GetGlobalVarsPtr</c>
/// slot), so <c>Globals.IsAvailable</c> is true in a live session.
/// <see cref="IEngineEnvironment.GetInterface"/> asks the side's shim root for
/// the engine's <c>appSystemFactory</c>, the factory a <c>CreateInterfaceFn</c>
/// handed to a game DLL is. It answered zero until plan_shim_areas_split phase
/// 7: there was no slot for the factory when this was written, and the side's
/// root, which now answers every other acquire, was not reachable from here.
/// A side with no root attached (before <c>LoadSide</c>, or a test with no
/// native half) still answers zero.
/// </para>
///
/// <para>
/// <b>Built over its side, and only over its side.</b> Each side compiles its
/// own copy of this class and registers one instance per composition, so two
/// sides in one process each read their own globals and ask their own root. A
/// server reading the client's <c>curtime</c> would be a plausible number
/// rather than a crash, which is why there is no constructor that takes a
/// pointer or a resolver: a test builds one through its side too.
/// </para>
/// </summary>
internal sealed class EngineEnvironment(EngineSide side, NativeInterfaceFactory interfaces) : IEngineEnvironment
{
    private readonly EngineSide _side = side ?? throw new ArgumentNullException(nameof(side));
    private readonly NativeInterfaceFactory _interfaces = interfaces ?? throw new ArgumentNullException(nameof(interfaces));

    /// <inheritdoc/>
    public nint GetInterface(string name)
    {
        // An empty name never reaches the factory: the engine compares the
        // pointer against every registered name, and an empty string is a
        // question with no useful answer.
        if (string.IsNullOrEmpty(name))
            return 0;

        return _interfaces.TryCreate(SourceSharpFactoryType.AppSystem, name, out nint self, out _) ? self : 0;
    }

    /// <inheritdoc/>
    public Globals Globals => new(_side.GlobalVarsPointer);
}
