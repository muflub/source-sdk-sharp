//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>Who hears one temp entity: a <see cref="TeFilter"/> as the recipients stock's filter class would hold.</summary>
public interface ITempEntRecipients
{
    /// <summary>CPVSFilter, CPASFilter, CPASAttenuationFilter, CSingleUserRecipientFilter or CBroadcastRecipientFilter.</summary>
    IRecipientList For(TeFilter filter);
}

/// <summary>The send itself: one temp entity through a built table to some recipients.</summary>
public interface ITempEntSender
{
    /// <summary>PlaybackTempEntity, now.</summary>
    void Playback(IRecipientList recipients, float delay, ReadOnlySpan<byte> blob, in TempEntTable table);
}

/// <summary>
/// The server's temp entity engine (plan_server_native_v2 T4b): what
/// shell_tempents.cpp decided, in C#. <see cref="TempEntRegister"/> plans a
/// declaration (<see cref="TeSchemaRegistry"/>'s refusals and event-cap price,
/// <see cref="TePropPlanner"/>'s props, <see cref="SendPropPlan"/>'s SendProp
/// fields) and has native store it; <see cref="TempEntPlayback"/> refuses what
/// SourceSharp_TempEntPlayback refused, clamps the delay to stock's [-1, 1], and
/// sends through the engine forwarder with a managed recipient filter;
/// <see cref="EffectDispatchIndex"/> asks the EffectDispatch string table. Every
/// other member is the engine's.
/// </summary>
public sealed class ServerTempEntEngine : EngineEffectsForwarder, ITempEntRegistrar
{
    private readonly ITempEntTables _tables;
    private readonly IEffectDispatchNames? _effectNames;
    private readonly ILog _log;
    private readonly TeSchemaRegistry _schema = new();
    private readonly Dictionary<string, int> _builtByTable = new(StringComparer.Ordinal);
    private readonly Dictionary<int, (TempEntTable Table, TeClass Class)> _built = new();

    private ITempEntSender? _sender;
    private ITempEntRecipients? _recipients;
    private bool _saidNoSender;

    public ServerTempEntEngine(IEngineEffects inner, ITempEntTables tables, ILog log,
                               IEffectDispatchNames? effectNames = null)
        : base(inner)
    {
        _tables = tables ?? throw new ArgumentNullException(nameof(tables));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _effectNames = effectNames;
    }

    /// <summary>
    /// The send and the recipients, which exist only once the managed root has
    /// built the recipient filter (ServerModule.BuildSounds), after the
    /// registration window. Until then a playback says so once and sends nothing.
    /// </summary>
    public void Attach(ITempEntSender sender, ITempEntRecipients recipients)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _recipients = recipients ?? throw new ArgumentNullException(nameof(recipients));
    }

    /// <summary>The table a handle names, if this engine built it.</summary>
    public TempEntTable? Table(int handle) => _built.TryGetValue(handle, out var entry) ? entry.Table : null;

    /// <inheritdoc/>
    public override int TempEntRegister(TeClass description)
    {
        ArgumentNullException.ThrowIfNull(description);

        int schemaHandle = _schema.Add(description, out TeRefusal why);

        if (schemaHandle < 0)
        {
            _log.Warning($"[SourceSharp] temp entity '{description.ClassName}' ({description.TableName}) NOT registered: "
                         + $"{TeSchemaRegistry.RefusalText(why)}\n");
            return -1;
        }

        IReadOnlyList<TePropPlan>? plan = TePropPlanner.Plan(description);

        if (plan is null)
        {
            _log.Warning($"[SourceSharp] temp entity '{description.ClassName}': its description cannot be planned; "
                         + "NOT registered\n");
            return -1;
        }

        var rows = new List<SendPropRow>(plan.Count);

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

            if (SendPropPlan.FromPlan(prop, nested) is not { } row)
            {
                _log.Warning($"[SourceSharp] temp entity '{description.ClassName}': prop '{prop.Name}' could not be "
                             + "built as a SendProp; NOT registered\n");
                return -1;
            }

            // SPROP_COLLAPSIBLE ON A NESTED TABLE CRASHES THE CLIENT: the receiver
            // applies a collapsed datatable at the object base, over whatever is
            // at offset 0 -- a vtable pointer on any native shell. Stock never
            // reaches it (SENDINFO_DT is past a vtable); TeClassBuilder pads for it,
            // and this says so if the pad stops arriving. The baseclass prop is at
            // zero on purpose and exempt.
            if (prop.Kind == TePropKind.DataTable && prop.Field >= 0 && (row.Flags & SendPropPlan.Collapsible) != 0)
            {
                _log.Warning($"[SourceSharp] temp entity '{description.ClassName}': nested table prop '{prop.Name}' "
                             + $"came out SPROP_COLLAPSIBLE (offset {prop.Offset}). The receiver applies a collapsed "
                             + "datatable at the OBJECT BASE, so its props will write over whatever is at offset 0 -- a "
                             + "vtable pointer, on any native shell. The nested table needs a non-zero offset; see "
                             + "TeClassBuilder's pad.\n");
            }

            rows.Add(row);
        }

        uint shape = TeSchemaRegistry.ShapeHash(description);
        byte[] block = TempEntBlock.Write(description.ClassName, description.TableName, description.RegistersClass,
                                          shape, rows);

        if (_tables.Build(block) is not { } table)
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
                         + "and cannot change without a restart; the old shape is what will be sent.\n");
        }

        _builtByTable[description.TableName] = table.Handle;
        _built[table.Handle] = (table, description);

        int worst = _schema.WorstCaseBits(schemaHandle);

        _log.Msg($"[SourceSharp] temp entity {table.Handle,2} '{description.ClassName}' -> '{description.TableName}': "
                 + $"{rows.Count} prop(s), {description.BlobBytes} blob byte(s), base '{description.BaseTable ?? "(none)"}', "
                 + (worst < 0 ? "worst case not priced" : $"worst case {worst} bit(s), {(worst + 7) / 8} byte(s) of {TePropPlanner.MaxEventBytes}")
                 + (table.Existed ? " (built by an earlier load)" : "") + "\n");

        return table.Handle;
    }

    /// <inheritdoc/>
    public override void TempEntPlayback(int handle, ReadOnlySpan<byte> blob, TeFilter filter, float delay)
    {
        if (blob.IsEmpty || !_built.TryGetValue(handle, out var entry))
            return;

        TeClass declaration = entry.Class;

        // A SHORT BLOB IS REFUSED, not truncated: the engine encodes through the
        // props' own offsets and has no idea how long the buffer is.
        if (blob.Length < declaration.BlobBytes)
        {
            _log.Warning($"[SourceSharp] temp entity '{declaration.ClassName}': {blob.Length} byte(s) offered for a "
                         + $"{declaration.BlobBytes} byte blob; NOT sent\n");
            return;
        }

        // A table with no ServerClass has no class id to send under: a base
        // table asked to play is a caller bug.
        if (entry.Table.ClassIdAddress == 0)
        {
            _log.Warning($"[SourceSharp] temp entity '{declaration.ClassName}' has no ServerClass of its own and "
                         + "cannot be played back\n");
            return;
        }

        // Stock's own bound (basetempentity.cpp: Assert( delay >= -1 && delay <= 1 )),
        // enforced rather than asserted.
        if (delay < -1f || delay > 1f)
        {
            _log.Warning($"[SourceSharp] temp entity '{declaration.ClassName}': delay {delay:F2} is outside stock's "
                         + "[-1,1]; clamped\n");
            delay = Math.Clamp(delay, -1f, 1f);
        }

        if (_sender is null || _recipients is null)
        {
            if (!_saidNoSender)
                _log.Warning("[SourceSharp] temp entities: no sender yet (the recipient filter is built with the "
                             + "managed root); nothing is sent until it is\n");

            _saidNoSender = true;
            return;
        }

        _sender.Playback(_recipients.For(filter), delay, blob, entry.Table);
    }

    /// <summary>
    /// An effect name as the EffectDispatch string table's index: AddString,
    /// which answers the index a name already has, so one name is one number for
    /// the level. 0 (the table's first entry) for no name, no table or a full one,
    /// as the native slot answered.
    /// </summary>
    public override int EffectDispatchIndex(string name)
    {
        if (string.IsNullOrEmpty(name) || _effectNames is null)
            return 0;

        int index = _effectNames.IndexOf(name);
        return index < 0 ? 0 : index;
    }
}

/// <summary>
/// <see cref="ITempEntRecipients"/> over the server's recipient rules
/// (<see cref="SoundRecipients"/>): the filter classes shell_tempents.cpp's
/// PlaybackThrough constructed, by kind.
/// </summary>
public sealed class TempEntRecipients(SoundRecipients recipients) : ITempEntRecipients
{
    /// <inheritdoc/>
    public IRecipientList For(TeFilter filter) => filter.Kind switch
    {
        TeFilterKind.Pvs            => recipients.Pvs(filter.Origin),
        TeFilterKind.Pas            => recipients.Pas(filter.Origin),
        TeFilterKind.PasAttenuation => recipients.PasAttenuation(filter.Origin,
                                           filter.Attenuation > 0f ? filter.Attenuation : SoundRecipients.AttnNorm),
        TeFilterKind.SinglePlayer   => Single(filter.PlayerIndex),
        _                           => recipients.AllPlayers(),
    };

    private RecipientFilter Single(int playerIndex)
    {
        RecipientFilter filter = recipients.Empty();
        filter.Add(playerIndex);
        return filter;
    }
}

/// <summary><see cref="ITempEntSender"/> over the engine forwarder (Server.Interop's <see cref="TempEntSendEngine"/>).</summary>
public sealed class TempEntSender(TempEntSendEngine engine) : ITempEntSender
{
    /// <inheritdoc/>
    public void Playback(IRecipientList recipients, float delay, ReadOnlySpan<byte> blob, in TempEntTable table)
        => engine.Playback(recipients, delay, blob, table);
}
