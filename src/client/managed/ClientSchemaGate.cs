using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client;

/// <summary>
/// Whether a standalone entity's payload is decoded: the schema-hash gate the
/// client's standalone shell asks once per update (Part 4, Part 14 Step 6),
/// and the refusals it has made, for <c>cs_cl_schema_status</c> and the dev
/// API's nettables route.
/// </summary>
public interface IClientSchemaGate
{
    /// <summary>
    /// Decode this update or refuse it. <paramref name="published"/> and
    /// <paramref name="expected"/> are what this side's type table holds for
    /// <paramref name="typeId"/>; <paramref name="seen"/> is the hash the update
    /// arrived with.
    /// </summary>
    bool Accept(uint typeId, bool published, uint expected, uint seen, int entIndex, string className);

    /// <summary>How many updates were refused, and the types they were spread over (the first <see cref="ClientSchemaGate.MaxTypes"/>).</summary>
    (int RefusedUpdates, IReadOnlyList<ClientSchemaRefusalRow> Types) Refusals();
}

/// <summary>
/// The gate. A hash that is not this build's is refused, not decoded: the two
/// builds' C# disagree about where the type's fields live, and decoding anyway
/// would put plausible values in the wrong fields with nothing reporting it.
/// One per client composition, from the container.
/// </summary>
public sealed class ClientSchemaGate(ILog log) : IClientSchemaGate
{
    /// <summary>
    /// The refused types kept. A build mismatch is a property of the build, so
    /// the types it can affect are the types there are; past this many the
    /// first ones have already made the point.
    /// </summary>
    public const int MaxTypes = 16;

    private readonly List<ClientSchemaRefusalRow> _types = [];
    private int _updates;

    /// <inheritdoc/>
    public bool Accept(uint typeId, bool published, uint expected, uint seen, int entIndex, string className)
    {
        // A type this side never published is no mismatch and no error: a class
        // may exist on the server with no client half, and those have always
        // decoded nothing. Refused, not counted.
        if (!published)
            return false;

        if (expected == seen)
            return true;

        _updates++;

        for (int i = 0; i < _types.Count; i++)
        {
            if (_types[i].TypeId != typeId)
                continue;

            // Said once per type rather than once per update: the second line
            // adds nothing and the ten-thousandth buries everything else.
            _types[i] = _types[i] with { Count = _types[i].Count + 1 };
            return false;
        }

        if (_types.Count < MaxTypes)
            _types.Add(new ClientSchemaRefusalRow(typeId, expected, seen, 1, className));

        log.Warning($"[SourceSharp/Client] SCHEMA MISMATCH on entity {entIndex}: '{className}' (type 0x{typeId:x8}) arrived " +
                    $"with layout fingerprint 0x{seen:x8} and this build's registry says 0x{expected:x8}. " +
                    "THE PAYLOAD WAS NOT DECODED — the two builds' C# does not agree about where " +
                    "this type's fields live, and decoding anyway would put plausible values in " +
                    "the wrong fields with nothing reporting it. Rebuild both sides from the same " +
                    "SourceSharp.Shared. (cs_cl_schema_status)\n");

        return false;
    }

    /// <inheritdoc/>
    public (int RefusedUpdates, IReadOnlyList<ClientSchemaRefusalRow> Types) Refusals() => (_updates, _types.ToArray());
}

/// <summary>
/// Which types this client has refused updates for, and what the two builds
/// disagree about. The console is where a client's refusals can always be
/// read: the dev API that also shows them may not be served.
/// </summary>
[ConCommand("cs_cl_schema_status",
            Help = "Which types this client has refused updates for, and what the two builds disagree about")]
public sealed class ClientSchemaStatusCommand(ILog log, IClientSchemaGate gate, ClientNetSchemaTypes types) : IConCommand
{
    public void Execute(string args)
    {
        var (updates, refused) = gate.Refusals();
        log.Msg($"[SourceSharp] schema: {types.All.Count} type(s) declared here, {updates} refusal(s) over {refused.Count} type(s)\n");

        foreach (var r in refused)
        {
            log.Warning($"[SourceSharp]   '{r.ClassName}' (0x{r.TypeId:x8}): expected 0x{r.Expected:x8}, saw 0x{r.Seen:x8}, " +
                        $"{r.Count} update(s) refused\n");
        }

        if (refused.Count == 0)
            log.Msg("[SourceSharp]   nothing has been refused\n");
    }
}
