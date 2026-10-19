using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// The client's temp entity tables, planned in C# (the client half of
/// plan_server_native_v2 T4b): what shell_tempents_client.cpp's
/// SourceSharp_TempEntRegisterClient decided over tempent_schema.h, here.
/// <see cref="TempEntRegister"/> runs <see cref="TeSchemaRegistry"/>'s refusals
/// and event-cap price, <see cref="TePropPlanner"/>'s plan (the same one the
/// server's SendTables come from) and <see cref="RecvPropPlan"/>'s RecvProp
/// fields, and hands one block per table to native, which stores the RecvProps,
/// builds the RecvTable and, when the class asks for one, its ClientClass and
/// receiving shell. Native decides nothing.
///
/// <para>
/// NO STOCK TABLE IS CONSULTED, and that is a measurement: the native registry
/// asked whether a RecvTable or ClientClass of a name was still stock's, for a
/// staged port that has finished. client.so holds no stock temp entity table or
/// class at ab8f56ef2 (no <c>DT_TE*</c>, <c>DT_BaseBeam</c>,
/// <c>DT_BaseTempEntity</c> or <c>DT_EffectData</c> string in it; the live
/// registration said "0 still owned by a native table"), so a base can only
/// name a table registered here, as on the server.
/// </para>
/// </summary>
public sealed class ClientTempEntEngine : ITempEntRegistrar
{
    private readonly ITempEntRecvTables _tables;
    private readonly ILog _log;
    private readonly TeSchemaRegistry _schema = new();
    private readonly Dictionary<string, int> _builtByTable = new(StringComparer.Ordinal);

    public ClientTempEntEngine(ITempEntRecvTables tables, ILog log)
    {
        _tables = tables ?? throw new ArgumentNullException(nameof(tables));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>How many tables native built (or handed back) for this engine.</summary>
    public int Built => _builtByTable.Count;

    /// <inheritdoc/>
    public int TempEntRegister(TeClass description)
    {
        ArgumentNullException.ThrowIfNull(description);

        int schemaHandle = _schema.Add(description, out TeRefusal why);

        if (schemaHandle < 0)
        {
            _log.Warning($"[SourceSharp] temp entity '{description.ClassName}' ({description.TableName}) NOT registered "
                         + $"on the client: {TeSchemaRegistry.RefusalText(why)}\n");
            return -1;
        }

        IReadOnlyList<TePropPlan>? plan = TePropPlanner.Plan(description);

        if (plan is null)
        {
            _log.Warning($"[SourceSharp] temp entity '{description.ClassName}': its description cannot be planned; "
                         + "NOT registered\n");
            return -1;
        }

        var rows = new List<RecvPropRow>(plan.Count);

        foreach (TePropPlan prop in plan)
        {
            int nested = -1;

            if (prop.Kind == TePropKind.DataTable
                && (prop.SubTable is null || !_builtByTable.TryGetValue(prop.SubTable, out nested)))
            {
                _log.Warning($"[SourceSharp] temp entity '{description.ClassName}': prop '{prop.Name}' nests "
                             + $"'{prop.SubTable}', which no table built here carries; NOT registered\n");
                return -1;
            }

            if (RecvPropPlan.FromPlan(prop, nested) is not { } row)
            {
                _log.Warning($"[SourceSharp] temp entity '{description.ClassName}': prop '{prop.Name}' could not be "
                             + "built as a RecvProp; NOT registered\n");
                return -1;
            }

            // THE OFFSET-ZERO NESTED TABLE, REFUSED. The sender marks a nested
            // table at offset zero SPROP_COLLAPSIBLE, and the RECEIVER applies a
            // collapsed datatable at the object base: over a blob it decodes the
            // nested table over this class's own first field and plays a
            // plausible wrong effect. This side cannot read the flag off a
            // RecvProp, but both sides build from one plan, so it sees the
            // condition. The baseclass prop is at zero on purpose and exempt.
            if (prop.Kind == TePropKind.DataTable && prop.Field >= 0 && prop.Offset == 0)
            {
                _log.Warning($"[SourceSharp] temp entity '{description.ClassName}': nested table prop '{prop.Name}' is at "
                             + "offset 0, which makes the SENDER mark it SPROP_COLLAPSIBLE and the receiver apply it at "
                             + "the object base. NOT registered -- it would decode the nested table over this class's own "
                             + "first field. The nested table needs a non-zero offset; see TeClassBuilder's pad.\n");
                return -1;
            }

            rows.Add(row);
        }

        uint shape = TeSchemaRegistry.ShapeHash(description);
        byte[] block = TempEntRecvBlock.Write(description.ClassName, description.TableName,
                                              description.RegistersClass, shape, rows);

        if (_tables.Build(block, description.BlobBytes) is not { } table)
        {
            _log.Warning($"[SourceSharp] temp entity '{description.ClassName}': native refused to store its table "
                         + "(the registration window has closed, or the table limit is reached); NOT registered\n");
            return -1;
        }

        // A RESHAPED CLASS CANNOT BE HONOURED across a reload: the table on the
        // wire is the one built at DLL init.
        if (table.Existed && table.ShapeHash != shape)
        {
            _log.Warning($"[SourceSharp] temp entity '{description.ClassName}' was RESHAPED across a reload "
                         + $"(now '{description.TableName}', {description.BlobBytes} blob bytes, "
                         + $"{description.Fields.Length} field(s)). The table on the wire is the one built at DLL init "
                         + "and cannot change without a restart; the old shape is what will be decoded.\n");
        }

        _builtByTable[description.TableName] = table.Handle;

        int worst = _schema.WorstCaseBits(schemaHandle);

        _log.Msg($"[SourceSharp] temp entity {table.Handle,2} '{description.ClassName}' -> '{description.TableName}': "
                 + $"{rows.Count} prop(s), {description.BlobBytes} blob byte(s), base '{description.BaseTable ?? "(none)"}', "
                 + "worst case " + (worst < 0 ? "not priced" : "")
                 + (table.Existed ? " (built by an earlier load)" : "") + "\n");

        if (worst >= 0)
            _log.Msg($"[SourceSharp]     {worst} bit(s), {(worst + 7) / 8} byte(s) of {TePropPlanner.MaxEventBytes}\n");

        return table.Handle;
    }
}
