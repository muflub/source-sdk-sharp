using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// The one string table this mod declares — plan_gamedll.md Part 13,
/// plan_gamedll_after.md Phase A8.
///
/// <para>
/// <b>The whole declaration is this line.</b> There is no create call, no find
/// call and no <c>TABLEID</c>: <see cref="StringTableRegistry"/> walks the
/// <c>[StringTable]</c> declarations, makes the table on the server inside the
/// one window the engine allows a create in, and registers what answers for it
/// under this interface. A consumer injects <c>ManagedStringTable</c>.
/// </para>
///
/// <para>
/// <b>It lives in SHARED, and that is the point of one declaration serving both
/// sides.</b> The server creates it and writes to it; the client gets the
/// replicated copy and reads it. Both sides compile against this same line, the
/// way both sides compile against a <c>[UserMessage]</c> or a
/// <c>[StandaloneNetState]</c> — a declaration only one side can see is a table
/// only one side can name.
/// </para>
///
/// <para>
/// <b>Why the mod ships a table at all.</b> Part 13 made this ABI conditional
/// on some mod asking for a table; Phase A8 met the condition by decision, and
/// a seam with no user is a seam nobody has run. What it carries is the managed
/// side's ABI version — a real answer a connected client cannot otherwise get,
/// and one the two halves of the boundary have to agree on.
/// </para>
///
/// <para>
/// <b>32 entries</b>, which is a budget rather than a guess:
/// <c>networkstringtabledefs.h</c> caps a server at <c>MAX_TABLES</c> = 32
/// tables, and this one holds a single line per map. The engine sizes a table
/// when it creates it and there is no resize.
/// </para>
/// </summary>
[StringTable("SourceSharpManaged", 32)]
public interface ManagedStringTable : IEngineStringTable
{
}
