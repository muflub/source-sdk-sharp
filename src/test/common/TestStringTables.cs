using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Tests;

/// <summary>
/// The string-table half of a <see cref="ServerGameDll"/> under test: the
/// registry it drives the create pass through, and the mod's own declared table
/// injected into it.
///
/// <para>
/// Built the way the module builds it — <see cref="StringTableRegistry"/> over
/// the same strict double, registering the same <c>[StringTable]</c>
/// declaration — rather than by handing the class a stub. Two facts depend on
/// that being the real thing: that the create pass asks for the declared name
/// at the declared size, and that the entry is read back through the engine.
/// </para>
/// </summary>
internal static class TestStringTables
{
    internal static (StringTableRegistry Registry, ManagedStringTable Table) For(RecordingEngine engine)
    {
        var registry = new StringTableRegistry((IEngineStringTables)engine, new StringTableWriter((IEngineStringTables)engine), new RecordingLog());
        var services = new ServiceContainer();

        registry.Register(new[] { typeof(ManagedStringTable) }, services);

        return (registry, services.Require<ManagedStringTable>());
    }
}
