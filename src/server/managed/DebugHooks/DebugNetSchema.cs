using SourceSharp.Abi;
using Debug.Api.Server.Interfaces;

namespace SourceSharp.Server;

/// <summary>
/// <see cref="IDebugNetSchema"/> over the server's networked schema, size tiers,
/// refusal ledger and payload reader (group G5). Decoding a payload through its
/// layout is game logic, so it happens here and the dev API gets the values.
/// </summary>
public sealed class DebugNetSchema(
    Func<IReadOnlyList<NetSchemaType>> types,
    Func<INetTiers> tiers,
    Func<INetTypeRefusals?> refusals,
    Func<int, NetEntityBlob> payload) : IDebugNetSchema
{
    private readonly Func<IReadOnlyList<NetSchemaType>> _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly Func<INetTiers> _tiers = tiers ?? throw new ArgumentNullException(nameof(tiers));
    private readonly Func<INetTypeRefusals?> _refusals = refusals ?? throw new ArgumentNullException(nameof(refusals));
    private readonly Func<int, NetEntityBlob> _payload = payload ?? throw new ArgumentNullException(nameof(payload));

    public IReadOnlyList<DebugNetType> Types()
    {
        var tiers = _tiers();
        return _types().Select(t => Type(t, tiers)).ToArray();
    }

    public DebugNetType? Find(string key)
        => NetSchema.Find(_types(), key) is { } found ? Type(found, _tiers()) : null;

    public DebugNetTiers Tiers()
        => new(_tiers().For(NetShellKind.Standalone)
                       .Select(t => new DebugNetTier(t.Table, t.PayloadBytes, t.Words, t.Props, t.Live)).ToArray(),
               NetSchema.StandaloneSharedProps);

    public IReadOnlyList<DebugNetRefusal> Refusals()
        => (_refusals()?.All ?? []).Select(r => new DebugNetRefusal(r.ClassName, r.TypeId, r.LayoutBytes)).ToArray();

    public DebugNetPayload Payload(int index)
    {
        var read = _payload(index);
        var tiers = _tiers();
        var type = read.Found ? NetSchema.Find(_types(), read.ClassName) : null;
        var decoded = type is null
            ? []
            : NetSchema.Decode(type, read.Blob)
                       .Select(d => new DebugNetValue(Field(d.Field), d.Present, d.Value, d.Hex, d.Number)).ToArray();

        return new DebugNetPayload(
            read.Found, read.Index, read.Handle, read.ClassName, read.Blob,
            read.Agreement.Name(), read.Agreement.Agrees(), read.Agreement.Words, read.Agreement.FirstBadWord,
            read.Agreement.State != PayloadAgreementState.NotSupplied,
            type is null ? null : Type(type, tiers), decoded);
    }

    private static DebugNetType Type(NetSchemaType type, INetTiers tiers)
        => new(type.ClassName, type.TypeName, type.Shell == NetSchemaShell.Standalone, type.TableName,
               type.TypeId, type.SchemaHash, type.LayoutBytes, type.BlobBytes, type.PropCount, type.PayloadWords,
               type.Published,
               TierLive: tiers.For(type.Shell == NetSchemaShell.Standalone ? NetShellKind.Standalone
                                                                           : NetShellKind.NetEntity).Count > 0,
               Oversized: tiers.Ceiling(type.LayoutBytes) < 0,
               type.Fields.Select(Field).ToArray());

    private static DebugNetField Field(NetSchemaField field)
        => new(field.Name, field.CsType, field.Kind.ToString().ToLowerInvariant(), field.Offset, field.Bytes,
               field.Bits, field.BitShift, field.IsPacked,
               field.EnumType is { } e
                   ? new DebugNetEnum(e.Name, NetEnums.IsFlags(e),
                                    NetEnums.MembersOf(e).Select(m => new DebugNetEnumMember(m.Name, m.Value)).ToArray())
                   : null);
}
