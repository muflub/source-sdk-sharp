namespace SourceSharp.Abi;

/// <summary>
/// One live entity's networked state, through the layout the development API's
/// own routes read it through.
///
/// <para>
/// TWO CALLERS, ONE ANSWER, and that is the whole reason this is an interface
/// rather than two lines in a console command. <c>./debug-api nettable
/// &lt;class&gt;</c> asks for the layout and <c>./debug-api entity/&lt;i&gt;/payload</c>
/// decodes a live blob against it; <c>ent_dump</c> now wants the same two facts
/// as text. Written twice they would answer differently the first time either
/// changed — which has bitten this project repeatedly — so the console dump goes
/// through the SAME <see cref="NetSchema.Describe"/> list and the SAME
/// <see cref="NetPayloadReader"/> the routes do, and the only thing that differs
/// between them is the formatting.
/// </para>
///
/// <para>
/// An interface rather than static calls because the reads can only be right in
/// a running game: <see cref="NetPayloadReader.Read"/> walks the live entity
/// list and must run on the game thread. Behind a contract, the describer above
/// it is a pure function of metadata and bytes, and is testable without one.
/// </para>
/// </summary>
public interface INetState
{
    /// <summary>
    /// The networked schema for a classname, or null when this side describes
    /// none — which is the ordinary answer for most native classes and is not a
    /// failure.
    /// </summary>
    NetSchemaType? SchemaFor(string className);

    /// <summary>
    /// One live entity's bytes by index. Must run on the game thread.
    /// <see cref="NetEntityBlob.Missing"/> for an index nothing answers to.
    /// </summary>
    NetEntityBlob Read(int index);
}

/// <summary>
/// The game's answer: the module's own schema list, and the live blob reader.
///
/// <para>
/// The list is a delegate rather than a copy because the module builds it lazily
/// and once — a second copy here would be a second list that could disagree with
/// the one the development API serves.
/// </para>
/// </summary>
public sealed class LiveNetState : INetState
{
    private readonly Func<IReadOnlyList<NetSchemaType>> _types;
    private readonly Func<int, NetEntityBlob> _read;

    public LiveNetState(Func<IReadOnlyList<NetSchemaType>> types, Func<int, NetEntityBlob> read)
    {
        _types = types;
        _read = read;
    }

    public NetSchemaType? SchemaFor(string className)
        => NetSchema.Find(_types(), className);

    public NetEntityBlob Read(int index) => _read(index);
}
