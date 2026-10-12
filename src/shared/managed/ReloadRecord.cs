using System.Collections.Generic;
using System.Globalization;

namespace SourceSharp.Abi;

/// <summary>
/// THE STATE OF SOMETHING THAT IS NOT AN ENTITY, in a form that outlives the
/// assembly that produced it.
///
/// <para>
/// <see cref="SavedEntity"/> answers "what does this entity hold", and the
/// default derivation fills it from what the class DECLARES — its
/// <c>[ScriptVars]</c> keys, its <c>[NetState]</c> fields, its outputs. That
/// covers everything an entity declares about itself and nothing at all about
/// the state a SUBSYSTEM holds ABOUT entities: which entity is parented to
/// which, which pairs are touching, what is booked to think and when, which
/// door is half-way through its travel. None of that is a field on an entity,
/// so no declaration on an entity can describe it — and every bit of it lives
/// in a service the unload destroys.
/// </para>
///
/// <para>
/// So this is the record a <see cref="IGameLifecycle"/> participant fills in
/// <c>OnSaveState</c> and reads back in <c>OnRestoreState</c>. One per
/// participant, keyed by its type name.
/// </para>
///
/// <para>
/// <b>STRINGS AND NOTHING ELSE, for exactly <see cref="SavedEntity"/>'s
/// reason.</b> It crosses a reload as bytes (<see cref="ReloadCarrier.PutPart"/>
/// through <see cref="SavedEntityCodec.EncodeParts"/>), and only strings have a
/// byte form that means the same thing to the build that reads it back.
/// </para>
///
/// <para>
/// <b>SCALARS AND ROWS, because subsystem state is nearly always a LIST.</b>
/// One parent link per parented child, one mover per moving entity, one
/// booking per scheduled think. A bag of <c>link.17.child</c> keys would work
/// and would put the row count in the key names, where nothing can count it;
/// <see cref="Rows"/> makes "how many did we carry" a number rather than a
/// parse.
/// </para>
/// </summary>
public sealed class ReloadRecord
{
    internal static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private readonly Dictionary<string, string> _scalars = new(System.StringComparer.Ordinal);
    private readonly List<ReloadRow> _rows = new();

    /// <summary>Whose record this is: the participant's type name.</summary>
    public string Owner { get; }

    public ReloadRecord(string owner) => Owner = owner ?? string.Empty;

    /// <summary>The one-off values, by key.</summary>
    public IReadOnlyDictionary<string, string> Scalars => _scalars;

    /// <summary>The repeated ones, in the order they were written.</summary>
    public IReadOnlyList<ReloadRow> Rows => _rows;

    /// <summary>Nothing was carried. Distinguished from "no record at all" by the caller.</summary>
    public bool IsEmpty => _scalars.Count == 0 && _rows.Count == 0;

    /// <summary>
    /// How many values this record carries in total — scalars plus every row's
    /// keys. This is the number the reload report counts, so that a participant
    /// whose save silently stopped writing reads as a FALL rather than as an
    /// absence nobody looks for.
    /// </summary>
    public int FieldCount
    {
        get
        {
            int n = _scalars.Count;
            foreach (var row in _rows)
                n += row.Count;
            return n;
        }
    }

    /// <summary>
    /// The bytes of key and value text this record holds, as UTF-16 chars
    /// rather than as an encoding nobody here writes. A size rather than a
    /// count, because "carried 4 fields" and "carried 4 fields of 90KB" are
    /// different answers to "is this cheap".
    /// </summary>
    public int ByteCount
    {
        get
        {
            int n = 0;
            foreach (var (k, v) in _scalars)
                n += (k.Length + v.Length) * sizeof(char);
            foreach (var row in _rows)
                n += row.ByteCount;
            return n;
        }
    }

    //-------------------------------------------------------------------------
    // Scalars.
    //-------------------------------------------------------------------------

    public void Set(string key, string? value) => _scalars[key] = value ?? string.Empty;
    public void Set(string key, int value) => _scalars[key] = value.ToString(Culture);
    public void Set(string key, long value) => _scalars[key] = value.ToString(Culture);
    public void Set(string key, float value) => _scalars[key] = value.ToString(Culture);
    public void Set(string key, bool value) => _scalars[key] = value ? "1" : "0";

    public bool Has(string key) => _scalars.ContainsKey(key);

    public string GetString(string key, string fallback = "")
        => _scalars.TryGetValue(key, out var v) ? v : fallback;

    public int GetInt(string key, int fallback = 0)
        => _scalars.TryGetValue(key, out var v) && ScriptedVarParse.Int(v, out int r) ? r : fallback;

    public long GetLong(string key, long fallback = 0)
        => _scalars.TryGetValue(key, out var v)
           && long.TryParse(v, NumberStyles.Integer, Culture, out long r) ? r : fallback;

    public float GetFloat(string key, float fallback = 0f)
        => _scalars.TryGetValue(key, out var v) && ScriptedVarParse.Float(v, out float r) ? r : fallback;

    public bool GetBool(string key, bool fallback = false)
        => _scalars.TryGetValue(key, out var v) && ScriptedVarParse.Bool(v, out bool r) ? r : fallback;

    //-------------------------------------------------------------------------
    // Rows.
    //-------------------------------------------------------------------------

    /// <summary>A new, empty row at the end. Fill it and it is carried.</summary>
    public ReloadRow AddRow()
    {
        var row = new ReloadRow();
        _rows.Add(row);
        return row;
    }

    /// <summary>
    /// Takes a row that has already been built. Used by the codec and by a
    /// restore that is replaying a block rather than a live save.
    /// </summary>
    public void AddRow(ReloadRow row)
    {
        if (row is not null)
            _rows.Add(row);
    }
}

/// <summary>
/// One repeated thing inside a <see cref="ReloadRecord"/>: one parent link, one
/// mover, one touching pair, one booked think.
///
/// <para>
/// Strings for <see cref="ReloadRecord"/>'s reason, which is the whole of why
/// this type exists rather than a tuple: a tuple of a side's own enum, or of a
/// <see cref="EntityHandle"/>-shaped struct declared in the side's assembly,
/// would pin the context. Handles go across as their bare <c>uint</c>.
/// </para>
/// </summary>
public sealed class ReloadRow
{
    private readonly Dictionary<string, string> _fields = new(System.StringComparer.Ordinal);

    /// <summary>What this row holds, by key.</summary>
    public IReadOnlyDictionary<string, string> Fields => _fields;

    /// <summary>How many keys. See <see cref="ReloadRecord.FieldCount"/>.</summary>
    public int Count => _fields.Count;

    /// <inheritdoc cref="ReloadRecord.ByteCount"/>
    public int ByteCount
    {
        get
        {
            int n = 0;
            foreach (var (k, v) in _fields)
                n += (k.Length + v.Length) * sizeof(char);
            return n;
        }
    }

    public ReloadRow Set(string key, string? value)
    {
        _fields[key] = value ?? string.Empty;
        return this;
    }

    public ReloadRow Set(string key, int value)
        => Set(key, value.ToString(ReloadRecord.Culture));

    public ReloadRow Set(string key, float value)
        => Set(key, value.ToString(ReloadRecord.Culture));

    public ReloadRow Set(string key, bool value) => Set(key, value ? "1" : "0");

    /// <summary>
    /// An entity, as the bare handle value.
    ///
    /// <para>
    /// <b>THE WHOLE HANDLE, NEVER THE INDEX.</b> An index is reused the instant
    /// its entity is freed, so an index written here and read back after a
    /// reload names whatever took the slot. The serial half is what makes the
    /// answer "the same entity or nothing", and <see cref="GetHandle"/>'s
    /// fallback is <see cref="EntityHandle.None"/> —
    /// <c>INVALID_EHANDLE_INDEX</c> — and NOT <c>default</c>, which is entry
    /// zero and a real slot holding worldspawn.
    /// </para>
    /// </summary>
    public ReloadRow Set(string key, EntityHandle value)
        => Set(key, value.Value.ToString(ReloadRecord.Culture));

    public ReloadRow Set(string key, Vector value)
        => Set(key, ScriptedVarParse.Save(value));

    public bool Has(string key) => _fields.ContainsKey(key);

    public string GetString(string key, string fallback = "")
        => _fields.TryGetValue(key, out var v) ? v : fallback;

    public int GetInt(string key, int fallback = 0)
        => _fields.TryGetValue(key, out var v) && ScriptedVarParse.Int(v, out int r) ? r : fallback;

    public float GetFloat(string key, float fallback = 0f)
        => _fields.TryGetValue(key, out var v) && ScriptedVarParse.Float(v, out float r) ? r : fallback;

    public bool GetBool(string key, bool fallback = false)
        => _fields.TryGetValue(key, out var v) && ScriptedVarParse.Bool(v, out bool r) ? r : fallback;

    public Vector GetVector(string key, Vector fallback = default)
        => _fields.TryGetValue(key, out var v) && ScriptedVarParse.Vector(v, out var r) ? r : fallback;

    /// <inheritdoc cref="Set(string, EntityHandle)"/>
    public EntityHandle GetHandle(string key)
        => _fields.TryGetValue(key, out var v)
           && uint.TryParse(v, NumberStyles.Integer, ReloadRecord.Culture, out uint r)
            ? new EntityHandle(r)
            : EntityHandle.None;
}
