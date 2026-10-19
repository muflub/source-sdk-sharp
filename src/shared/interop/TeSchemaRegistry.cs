//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>Why a temp entity description was refused, or that it was not.</summary>
public enum TeRefusal
{
    /// <summary>Taken.</summary>
    Accepted = 0,

    /// <summary>The registration window has shut.</summary>
    Closed,

    /// <summary>No description, or no class or table name on it.</summary>
    Null,

    /// <summary>More classes than the registry holds.</summary>
    Full,

    /// <summary>A class of that name is already registered.</summary>
    DuplicateClass,

    /// <summary>A table of that name is already registered.</summary>
    DuplicateTable,

    /// <summary>More fields than the registry holds.</summary>
    FieldCount,

    /// <summary>Two props of one name in one table.</summary>
    DuplicateField,

    /// <summary>A field whose kind, width or storage size cannot be built.</summary>
    FieldShape,

    /// <summary>A field outside the declared blob.</summary>
    Offset,

    /// <summary>A base or nested table nobody has registered.</summary>
    Base,

    /// <summary>Worst case over the engine's 192-byte event cap.</summary>
    TooBig,
}

/// <summary>
/// One side's temp entity descriptions, and the refusals worth having: a
/// duplicate name, a field that cannot be built or lies outside its blob, a base
/// nobody owns, and a worst case over the engine's event cap. The managed port
/// of tempent_schema.h's <c>CSourceSharpTeRegistry</c>; the facts are that
/// class's gtests, case for case. Both sides register through it: the server's
/// ServerTempEntEngine and the client's ClientTempEntEngine.
///
/// <para>
/// NO EXTERNAL BASE RESOLVER, and that is a measurement rather than a shortcut:
/// the native registry asked a callback whether STOCK still registered a base
/// table, for a staged port that has finished. server.so holds no stock temp
/// entity table (no <c>DT_TE*</c>, <c>DT_BaseBeam</c> or <c>DT_EffectData</c>
/// string in it at 79be5cebf), and client.so holds none at ab8f56ef2 (nor a
/// <c>DT_BaseTempEntity</c>), so the only tables a base can name are the ones
/// registered here, on either side.
/// </para>
/// </summary>
public sealed class TeSchemaRegistry
{
    private readonly List<TeClass> _classes = new();
    private bool _closed;

    /// <summary>How many descriptions are held.</summary>
    public int Count => _classes.Count;

    /// <summary>Whether the window has shut.</summary>
    public bool IsClosed => _closed;

    /// <summary>Shuts the window: every later <see cref="Add"/> is refused.</summary>
    public void Close() => _closed = true;

    /// <summary>The description at a handle, or null.</summary>
    public TeClass? At(int handle) => handle >= 0 && handle < _classes.Count ? _classes[handle] : null;

    /// <summary>The handle of the class of that name, or -1.</summary>
    public int FindByClassName(string? name)
        => name is null ? -1 : _classes.FindIndex(c => string.Equals(c.ClassName, name, StringComparison.Ordinal));

    /// <summary>The handle of the table of that name, or -1.</summary>
    public int FindByTableName(string? name)
        => name is null ? -1 : _classes.FindIndex(c => string.Equals(c.TableName, name, StringComparison.Ordinal));

    /// <summary>
    /// The fingerprint a reload compares: the table's name, its blob and its
    /// field count, the three things a reshape changes (FNV-1a, 32 bits). Native
    /// keeps it beside a table it built and hands it back when a later load asks
    /// for the same class, because the table on the wire is the one built at DLL
    /// init.
    /// </summary>
    public static uint ShapeHash(TeClass declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        uint hash = 2166136261u;

        foreach (char c in $"{declaration.TableName}|{declaration.BlobBytes}|{declaration.Fields.Length}")
        {
            hash ^= (byte)c;
            hash *= 16777619u;
        }

        return hash;
    }

    /// <summary>The words a refusal is reported in (SourceSharpTe_RefusalText's).</summary>
    public static string RefusalText(TeRefusal why) => why switch
    {
        TeRefusal.Accepted       => "accepted",
        TeRefusal.Closed         => "the registration window has closed",
        TeRefusal.Null           => "no description, or no class/table name on it",
        TeRefusal.Full           => "more temp entity classes than the registry holds",
        TeRefusal.DuplicateClass => "a class of that name is already registered",
        TeRefusal.DuplicateTable => "a table of that name is already registered",
        TeRefusal.FieldCount     => "more fields than the registry holds",
        TeRefusal.DuplicateField => "two props of one name in one table",
        TeRefusal.FieldShape     => "a field whose kind, width or storage size cannot be built",
        TeRefusal.Offset         => "a field outside the declared blob",
        TeRefusal.Base           => "a base table nobody has registered",
        TeRefusal.TooBig         => "worst case over the engine's 192-byte event cap",
        _                        => "refused",
    };

    /// <summary>
    /// The worst case one send of this class costs, its base chain and nested
    /// tables included; -1 when a table in the chain is not held here, which is
    /// "not priced" rather than "priced at zero".
    /// </summary>
    public int WorstCaseBits(int handle)
    {
        int bits = TePropPlanner.MessageOverheadBits;
        int guard = 0;

        for (int i = handle; i >= 0 && guard < TePropPlanner.MaxClasses; guard++)
        {
            TeClass? declaration = At(i);

            if (declaration is null)
                return -1;

            foreach (TeField field in declaration.Fields)
            {
                if (field.Kind == TePropKind.DataTable)
                {
                    int nested = FindByTableName(field.SubTable);

                    if (nested < 0)
                        return -1;

                    int nestedBits = WorstCaseBits(nested);

                    if (nestedBits < 0)
                        return -1;

                    bits += nestedBits - TePropPlanner.MessageOverheadBits;
                    continue;
                }

                int value = TePropPlanner.FieldValueBits(field);

                if (value < 0)
                    return -1;

                bits += TePropPlanner.PropIndexBits * TePropPlanner.FieldPropCount(field) + value;
            }

            i = FindByTableName(declaration.BaseTable);

            if (i < 0 && !string.IsNullOrEmpty(declaration.BaseTable))
                return -1;
        }

        return bits;
    }

    /// <summary>
    /// Takes a description, or says why not. The handle is its position: the
    /// order a server registers in is the order its tables are built.
    /// </summary>
    public int Add(TeClass? declaration, out TeRefusal why)
    {
        why = Check(declaration);

        if (why != TeRefusal.Accepted)
            return -1;

        int handle = _classes.Count;

        _classes.Add(declaration!);

        int worst = WorstCaseBits(handle);

        if (worst >= 0 && worst > TePropPlanner.MaxEventBits)
        {
            _classes.RemoveAt(handle);
            why = TeRefusal.TooBig;
            return -1;
        }

        return handle;
    }

    private TeRefusal Check(TeClass? declaration)
    {
        if (_closed)
            return TeRefusal.Closed;

        if (declaration is null || string.IsNullOrEmpty(declaration.ClassName)
                                || string.IsNullOrEmpty(declaration.TableName))
            return TeRefusal.Null;

        if (_classes.Count >= TePropPlanner.MaxClasses)
            return TeRefusal.Full;

        if (FindByClassName(declaration.ClassName) >= 0)
            return TeRefusal.DuplicateClass;

        if (FindByTableName(declaration.TableName) >= 0)
            return TeRefusal.DuplicateTable;

        if (declaration.Fields.Length > TePropPlanner.MaxFields)
            return TeRefusal.FieldCount;

        if (declaration.BlobBytes < 0)
            return TeRefusal.Offset;

        if (!string.IsNullOrEmpty(declaration.BaseTable) && FindByTableName(declaration.BaseTable) < 0)
            return TeRefusal.Base;

        for (int f = 0; f < declaration.Fields.Length; f++)
        {
            TeField field = declaration.Fields[f];

            if (string.IsNullOrEmpty(field.Name))
                return TeRefusal.FieldShape;

            for (int g = 0; g < f; g++)
            {
                if (string.Equals(declaration.Fields[g].Name, field.Name, StringComparison.Ordinal))
                    return TeRefusal.DuplicateField;
            }

            if (field.Kind == TePropKind.DataTable)
            {
                if (string.IsNullOrEmpty(field.SubTable))
                    return TeRefusal.FieldShape;

                if (FindByTableName(field.SubTable) < 0)
                    return TeRefusal.Base;

                continue;
            }

            if (TePropPlanner.FieldValueBits(field) < 0)
                return TeRefusal.FieldShape;

            int bytes = TePropPlanner.FieldBlobBytes(field);

            if (bytes < 0)
                return TeRefusal.FieldShape;

            if (field.Offset < 0 || field.Offset + bytes > declaration.BlobBytes)
                return TeRefusal.Offset;
        }

        return TeRefusal.Accepted;
    }
}
