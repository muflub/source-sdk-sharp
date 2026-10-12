using System.Text;
using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <c>ent_name</c>, <c>ent_text</c>, <c>ent_info</c> and <c>ent_dump</c> — what
/// is known about an entity, as text.
///
/// <para>
/// Two depths, which is stock's own split: <c>ent_name</c> and <c>ent_text</c>
/// are a label and a position, and <c>ent_info</c> and <c>ent_dump</c> are
/// everything. Stock drew the first pair in the world with NDebugOverlay; that
/// has no managed crossing, so the same facts go to the console and every line
/// says so.
/// </para>
///
/// <para>
/// "EVERYTHING" NOW INCLUDES WHAT A MAP CAN SEND IT AND WHAT IT PUTS ON THE
/// WIRE. Those two are the questions a broken entity actually poses — "why did
/// my input do nothing" and "why does the client see something else" — and until
/// now the only way to ask either was the development API, which needs a second
/// terminal and a running HTTP server. They come from the SAME two places those
/// routes read: <see cref="IEntityDirectory.DescribeClasses"/> is what
/// <c>./debug-api classes</c> serves, and <see cref="INetState"/> is the layout
/// <c>./debug-api nettable</c> prints and the blob
/// <c>./debug-api entity/&lt;i&gt;/payload</c> decodes. Not a third way to ask the
/// same question — the same way, formatted for a console.
/// </para>
/// </summary>
public abstract class DescribeCommand : DevCommand
{
    /// <summary>
    /// How many networked fields a dump prints before it stops naming them.
    /// A player's state is a handful; a class with sixty fields would push the
    /// rest of the report off the top of a 50-line console, which is how a
    /// report that shows everything ends up showing nothing.
    /// </summary>
    public const int MaxNetFields = 40;

    protected DescribeCommand(DevContext context, INetState netState) : base(context)
    {
        NetState = netState;
    }

    /// <summary>The networked schema and the live blob, as the dev API reads them.</summary>
    protected INetState NetState { get; }

    /// <summary>Whether this spelling is one of the two that print everything.</summary>
    protected abstract bool Full { get; }

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        foreach (var subject in Select(issuer, Rest(argv, 0)))
            Log.Msg(Report(subject));
    }

    /// <summary>
    /// One entity's report. A string rather than a series of prints so that the
    /// whole of it is one thing a test can read — the netstate half is arithmetic
    /// over a blob, which is exactly the kind of thing that goes wrong quietly.
    /// </summary>
    public string Report(EntityHandle subject)
    {
        var report = new StringBuilder();
        report.Append($"{Verb}: {Describe(subject)}\n");
        report.Append($"    origin   {Context.Entities.GetOrigin(subject)}\n");
        report.Append($"    angles   {Context.Entities.GetAngles(subject)}\n");

        if (!Full)
            return report.ToString();

        var (mins, maxs) = Context.Entities.GetBounds(subject);
        report.Append($"    velocity {Context.Entities.GetVelocity(subject)}\n");
        report.Append($"    health   {Context.Entities.GetHealth(subject)}\n");
        report.Append($"    bounds   {mins} .. {maxs}\n");
        report.Append($"    flags    spawnflags {Context.Entities.GetSpawnFlags(subject)}\n");
        report.Append($"    managed  {(Context.Directory.Get(subject) is { } m ? m.GetType().Name : "no — native entity")}\n");

        string className = Context.Entities.GetClassName(subject);

        AppendIo(report, className);
        AppendNetState(report, subject, className);

        return report.ToString();
    }

    /// <summary>
    /// What a map may send this class and what it sends back — <c>./debug-api
    /// classes</c>'s two lists, for one class.
    ///
    /// <para>
    /// A class with no declaration is NAMED as such rather than left out. "This
    /// entity accepts no inputs" and "nothing on this side declares this class"
    /// are different answers, and only the second one explains why an ent_fire
    /// did nothing.
    /// </para>
    /// </summary>
    private void AppendIo(StringBuilder report, string className)
    {
        var declared = Context.Directory.DescribeClasses()
            .FirstOrDefault(c => string.Equals(c.ClassName, className, StringComparison.OrdinalIgnoreCase));

        if (declared.ClassName is null)
        {
            report.Append($"    inputs   — no managed class declares '{className}', so its I/O is " +
                          $"the native entity's and is not readable from here\n");
            return;
        }

        report.Append($"    inputs   {Listed(declared.Inputs)}\n");
        report.Append($"    outputs  {Listed(declared.Outputs)}\n");
    }

    /// <summary>
    /// What this entity puts on the wire, decoded through its own layout — the
    /// console form of <c>./debug-api entity/&lt;index&gt;/payload</c>.
    ///
    /// <para>
    /// Three separate absences, told apart, because each means something
    /// different: the class has no networked schema on this side; the schema
    /// exists but the entity's bytes could not be read; or the blob is shorter
    /// than the layout, which is a real fault and is reported per field by
    /// <see cref="NetSchema.Decode"/> rather than papered over with a zero.
    /// </para>
    /// </summary>
    private void AppendNetState(StringBuilder report, EntityHandle subject, string className)
    {
        var schema = NetState.SchemaFor(className);

        if (schema is null)
        {
            report.Append($"    netstate — no networked schema for '{className}' on this side\n");
            return;
        }

        int index = Context.Entities.GetIndex(subject);
        var blob = NetState.Read(index);

        report.Append($"    netstate {schema.TypeName} on {schema.TableName}, " +
                      $"{schema.Fields.Count} field(s), {schema.BlobBytes} byte blob" +
                      $"{(schema.Published ? "" : " (NOT published on this side)")}\n");

        if (!blob.Found)
        {
            report.Append($"        (no bytes: nothing answered to entity index {index})\n");
            return;
        }

        var decoded = NetSchema.Decode(schema, blob.Blob);

        foreach (var field in decoded.Take(MaxNetFields))
        {
            report.Append($"        {field.Field.Name,-24} {field.Field.CsType,-10} " +
                          $"@{field.Field.Offset,-4} " +
                          $"{(field.Present ? field.Value : "— past the end of a short blob")}\n");
        }

        if (decoded.Count > MaxNetFields)
            report.Append($"        ... {decoded.Count - MaxNetFields} more; " +
                          $"./debug-api entity/{index}/payload prints them all\n");
    }

    /// <summary>A list of names, or a phrase saying there are none.</summary>
    private static string Listed(IReadOnlyList<string> names)
        => names.Count > 0 ? string.Join(", ", names) : "none declared";
}

/// <summary><c>ent_name</c> — the label and where it is.</summary>
[ConCommand("ent_name", Help = "Prints the name of the given entity(s). (must have sv_cheats)")]
public sealed class EntNameCommand : DescribeCommand
{
    public EntNameCommand(DevContext context, INetState netState) : base(context, netState)
    {
    }

    protected override bool Full => false;
}

/// <summary><c>ent_text</c> — stock's debug-text spelling of the same report.</summary>
[ConCommand("ent_text", Help = "Prints debugging information about the given entity(s). (must have sv_cheats)")]
public sealed class EntTextCommand : DescribeCommand
{
    public EntTextCommand(DevContext context, INetState netState) : base(context, netState)
    {
    }

    protected override bool Full => false;
}

/// <summary><c>ent_info</c> — everything known, inputs and net state included.</summary>
[ConCommand("ent_info",
    Help = "Prints everything known about the given entity(s): state, bounds, the inputs and " +
           "outputs its class declares, and its networked fields with their live values. " +
           "(must have sv_cheats)")]
public sealed class EntInfoCommand : DescribeCommand
{
    public EntInfoCommand(DevContext context, INetState netState) : base(context, netState)
    {
    }

    protected override bool Full => true;
}

/// <summary><c>ent_dump</c> — everything known, inputs and net state included.</summary>
[ConCommand("ent_dump",
    Help = "Prints everything known about the given entity(s): state, bounds, the inputs and " +
           "outputs its class declares, and its networked fields with their live values. " +
           "(must have sv_cheats)")]
public sealed class EntDumpCommand : DescribeCommand
{
    public EntDumpCommand(DevContext context, INetState netState) : base(context, netState)
    {
    }

    protected override bool Full => true;
}
