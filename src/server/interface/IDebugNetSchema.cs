namespace SourceSharp.Server.Interface;

/// <summary>One member of a networked enum (group G5).</summary>
public sealed record DebugNetEnumMember(string Name, long Value);

/// <summary>A field's enum type: its name, whether it is a flags enum, and its members (group G5).</summary>
public sealed record DebugNetEnum(string TypeName, bool Flags, IReadOnlyList<DebugNetEnumMember> Members);

/// <summary>One networked field's place in the payload (group G5).</summary>
/// <param name="Kind">The field kind, lower case ("int", "vector", ...).</param>
/// <param name="Packed">Whether it is bit-packed inside its bytes.</param>
public sealed record DebugNetField(
    string Name, string CsType, string Kind, int Offset, int Bytes, int Bits, int BitShift, bool Packed,
    DebugNetEnum? Enum);

/// <summary>A C# type that networks state of its own (group G5).</summary>
/// <param name="Standalone">Its shell: standalone, or a native net entity.</param>
/// <param name="TierLive">Whether native registered any tier for its shell.</param>
/// <param name="Oversized">Whether its layout fits no tier.</param>
public sealed record DebugNetType(
    string ClassName, string TypeName, bool Standalone, string TableName, uint TypeId, uint SchemaHash,
    int LayoutBytes, int BlobBytes, int PropCount, int PayloadWords, bool Published, bool TierLive, bool Oversized,
    IReadOnlyList<DebugNetField> Fields);

/// <summary>A payload size tier native registered (group G5).</summary>
public sealed record DebugNetTier(string Table, int PayloadBytes, int Words, int Props, bool Live);

/// <summary>The standalone tiers, and the props every standalone table shares (group G5).</summary>
public sealed record DebugNetTiers(IReadOnlyList<DebugNetTier> Standalone, int SharedProps);

/// <summary>A type the server refused to network, and why: its layout's size (group G5).</summary>
public sealed record DebugNetRefusal(string ClassName, uint TypeId, int LayoutBytes);

/// <summary>One field decoded out of a payload (group G5).</summary>
public sealed record DebugNetValue(DebugNetField Field, bool Present, string Value, string Hex, double? Number);

/// <summary>One entity's networked bytes, read on the game thread and decoded through its type (group G5).</summary>
/// <param name="AgreementName">Whether the bytes agree with what native sends: "agrees", "disagrees", "none" or "unsupported".</param>
/// <param name="AgreementSupplied">False when native did not report an agreement at all.</param>
/// <param name="Type">Its networked type, or null when none matches its classname.</param>
public sealed record DebugNetPayload(
    bool Found, int Index, uint Handle, string ClassName, byte[] Blob,
    string AgreementName, bool Agrees, int AgreementWords, int FirstBadWord, bool AgreementSupplied,
    DebugNetType? Type, IReadOnlyList<DebugNetValue> Decoded);

/// <summary>The server's networked schema, its tiers and refusals, and one entity's payload (group G5).</summary>
public interface IDebugNetSchema
{
    /// <summary>Every networked type. Any thread.</summary>
    IReadOnlyList<DebugNetType> Types();

    /// <summary>A type by classname (any case), or by type id in decimal or hex; null when none.</summary>
    DebugNetType? Find(string key);

    DebugNetTiers Tiers();

    IReadOnlyList<DebugNetRefusal> Refusals();

    /// <summary>The payload of the entity at <paramref name="index"/>. Game thread only.</summary>
    DebugNetPayload Payload(int index);
}
