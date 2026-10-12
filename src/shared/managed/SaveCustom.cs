using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// THE THIRD TIER: a class that carries something the declarations cannot
/// describe, and hands over an object of its own to do it.
///
/// <para>
/// The three tiers, in the order a class should reach for them:
/// </para>
///
/// <list type="number">
/// <item><b>Automatic.</b> Declared map keys (<c>[ScriptVars]</c>), networked
/// fields (<c>[NetField]</c>), outputs, and the directory's I/O connections and
/// pending events. Nothing to write; a new class gets them and cannot
/// forget.</item>
/// <item><b>Marked.</b> A plain field or property with
/// <see cref="SaveFieldAttribute"/>. One line, and the value is carried.</item>
/// <item><b>Custom.</b> This interface, for state that is not a field at all —
/// a subsystem's table of links, a service's list of what it already placed, a
/// graph the class builds and wants back whole.</item>
/// </list>
///
/// <para>
/// <b>IMPLEMENTED BY AN ENTITY OR BY A SERVICE, and one interface for both.</b>
/// The registry checks every entity for it, and <c>LifecycleDriver</c> checks
/// every registered <see cref="IGameLifecycle"/> participant. Two names for one
/// idea would have been a second vocabulary for the next person to learn and to
/// pick the wrong half of.
/// </para>
///
/// <para>
/// <b>WHAT CROSSES IS THE SHAPE, NEVER THE INSTANCE.</b> This is the point
/// people get wrong about a hot reload and it is not a limitation of the
/// writer: the type you hand over is <i>defined by the assembly being
/// unloaded</i>. Keeping the object would keep that assembly alive forever —
/// the leak <see cref="ManagedCaches"/> exists to prevent — and, worse, the
/// object you got back would be an instance of the OLD class, which is the one
/// thing a reload exists to replace. So <see cref="ISaveWriter.Write"/> reads
/// the object's shape into text, and <see cref="ISaveReader.Read{T}"/> builds a
/// FRESH instance of the NEW build's type and fills it. A class that changed
/// shape between the two builds is handled by
/// <see cref="SaveGraph"/>'s three arms, and every one of them is reported.
/// </para>
/// </summary>
public interface ISaveCustom
{
    /// <summary>
    /// Write whatever must survive. Called before the assembly is unloaded, and
    /// before a save block is written.
    /// </summary>
    void OnSave(ISaveWriter writer);

    /// <summary>
    /// Read it back. Called after the new assembly is up and after the entity
    /// rows are restored, so a handle written here names an entity that exists
    /// again.
    ///
    /// <para>
    /// The reader is never null and is EMPTY rather than absent when nothing
    /// was carried — a first run, a build that did not save, a save that was
    /// refused. So an implementation reads with fallbacks rather than testing
    /// for a reader.
    /// </para>
    /// </summary>
    void OnRestore(ISaveReader reader);
}

/// <summary>What <see cref="ISaveCustom.OnSave"/> writes into.</summary>
public interface ISaveWriter
{
    /// <summary>A number, a string, a flag, a vector, a handle.</summary>
    void Write(string key, string? value);

    /// <inheritdoc cref="Write(string, string?)"/>
    void Write(string key, int value);

    /// <inheritdoc cref="Write(string, string?)"/>
    void Write(string key, float value);

    /// <inheritdoc cref="Write(string, string?)"/>
    void Write(string key, bool value);

    /// <inheritdoc cref="Write(string, string?)"/>
    void Write(string key, Vector value);

    /// <summary>
    /// An entity, as the whole handle.
    ///
    /// <para>
    /// NEVER AS AN INDEX: an index is reused the instant its entity is freed,
    /// so an index read back after a reload names whatever took the slot. And
    /// the spelling of "nothing" is <see cref="EntityHandle.None"/> —
    /// <c>INVALID_EHANDLE_INDEX</c>, which is also <c>default</c> — never the
    /// raw handle 0, which is entry ZERO, a real slot holding worldspawn.
    /// </para>
    /// </summary>
    void Write(string key, EntityHandle value);

    /// <summary>
    /// AN OBJECT OF THE CALLER'S OWN CLASS, written as its shape.
    ///
    /// <para>
    /// See <see cref="SaveGraph"/> for exactly what is supported and what is
    /// refused. An unsupported type is REFUSED BY NAME and nothing is written
    /// for it — never a silent no-op, which would be a class that believes it
    /// saved its state and did not.
    /// </para>
    /// </summary>
    void WriteObject(string key, object? value);

    /// <summary>
    /// A repeated thing: one link, one mover, one booking. Fill the row and it
    /// is carried, in the order the rows were made.
    /// </summary>
    ReloadRow AddRow();
}

/// <summary>What <see cref="ISaveCustom.OnRestore"/> reads out of.</summary>
public interface ISaveReader
{
    /// <summary>Whether this key was carried at all — not the same as what it holds.</summary>
    bool Has(string key);

    string ReadString(string key, string fallback = "");
    int ReadInt(string key, int fallback = 0);
    float ReadFloat(string key, float fallback = 0f);
    bool ReadBool(string key, bool fallback = false);
    Vector ReadVector(string key, Vector fallback = default);

    /// <summary>
    /// An entity, RE-CHECKED rather than assumed.
    ///
    /// <para>
    /// A reload keeps the native world standing, so a handle still names its
    /// entity; a SAVE's does not, because every serial advances when the world
    /// is rebuilt. So this asks the engine, and answers
    /// <see cref="EntityHandle.None"/> (also <c>default</c>) for a handle that
    /// names nothing — never the raw handle 0, which is entry zero.
    /// </para>
    /// </summary>
    EntityHandle ReadHandle(string key);

    /// <summary>
    /// A FRESH instance of <typeparamref name="T"/>, filled from what was
    /// written. Never the object that was written — see
    /// <see cref="ISaveCustom"/> for why that would be wrong rather than merely
    /// hard.
    ///
    /// <para>
    /// Null when nothing was written under that key, or when the record could
    /// not be read into this build's <typeparamref name="T"/> — and the second
    /// case is reported by name.
    /// </para>
    /// </summary>
    T? ReadObject<T>(string key) where T : class;

    /// <summary>The rows, in the order they were written. Empty, never null.</summary>
    IReadOnlyList<ReloadRow> Rows { get; }

    /// <summary>
    /// A handle read out of a ROW, re-checked exactly as
    /// <see cref="ReadHandle"/> re-checks a keyed one, against the same
    /// side's engine (plan_abi_hotreload D8).
    /// </summary>
    EntityHandle Resolve(EntityHandle handle);
}

/// <summary>
/// How a restore re-checks the handles it reads: against ONE side's engine,
/// the one the restoring composition owns (plan_abi_hotreload D8). It was a
/// static on <see cref="SaveGraph"/> that asked whichever side was active.
/// </summary>
public sealed class SavedHandles
{
    private readonly IEngineHost _host;
    private readonly IEngineEntities _entities;

    public SavedHandles(IEngineHost host, IEngineEntities entities)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
    }

    /// <summary>
    /// A handle out of a record, checked against the world.
    ///
    /// <para>
    /// <b>ENTRY ZERO IS A REAL SLOT.</b> The raw handle 0 is index 0 serial
    /// 0, which can be worldspawn — so a handle that failed to come back must
    /// answer <see cref="EntityHandle.None"/> (<c>INVALID_EHANDLE_INDEX</c>,
    /// and <c>default</c> since 2026-10-05) and never entry zero. Two lanes on
    /// this plan measured the wrong path by getting that backwards.
    /// </para>
    ///
    /// <para>
    /// WITH NO ENGINE the handle is returned as written, which is the same split
    /// <c>RestoreFrom</c> and <c>NetState.Bind</c> make: "this handle names
    /// nothing" is a real answer only when there is something to ask, and the
    /// hermetic tests drive this with no native side under it.
    /// </para>
    /// </summary>
    public EntityHandle Resolve(EntityHandle handle)
    {
        if (handle.Value == EntityHandle.None.Value)
            return EntityHandle.None;

        if (!_host.IsInitialized)
            return handle;

        return _entities.IsValid(handle) ? handle : EntityHandle.None;
    }
}

/// <summary>
/// The writer and the reader over one <see cref="ReloadRecord"/>. One class for
/// both directions, because the two halves have to agree on the key spelling
/// and two classes is where they stop agreeing.
/// </summary>
public sealed class SaveRecordAccess : ISaveWriter, ISaveReader
{
    private readonly ReloadRecord _record;
    private readonly string _owner;
    private readonly SavedHandles? _handles;
    private readonly ILog _log;

    /// <param name="handles">
    /// How the reading half re-checks handles: the restoring side's engine.
    /// Only a writer may leave it out; a read that needs it refuses.
    /// </param>
    /// <param name="log">Where a value that cannot be carried is said (statics to zero).</param>
    public SaveRecordAccess(ReloadRecord record, string owner, ILog log, SavedHandles? handles = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _record = record ?? throw new ArgumentNullException(nameof(record));
        _owner = owner;
        _handles = handles;
    }

    private SavedHandles Handles
        => _handles ?? throw new InvalidOperationException(
            $"{_owner}: this record was opened for writing; reading a handle needs the side's SavedHandles");

    /// <summary>The record being written or read. For the report and the store.</summary>
    public ReloadRecord Record => _record;

    public void Write(string key, string? value) => _record.Set(key, value);
    public void Write(string key, int value) => _record.Set(key, value);
    public void Write(string key, float value) => _record.Set(key, value);
    public void Write(string key, bool value) => _record.Set(key, value);
    public void Write(string key, Vector value) => _record.Set(key, ScriptedVarParse.Save(value));
    public void Write(string key, EntityHandle value)
        => _record.Set(key, value.Value.ToString(ReloadRecord.Culture));

    public void WriteObject(string key, object? value)
    {
        if (value is null)
        {
            _record.Set(key, string.Empty);
            return;
        }

        string? text = SaveGraph.Write(value, _owner, key, _log);

        // REFUSED, NOT EMPTY. SaveGraph has already said by name what it could
        // not carry; writing an empty value on top of that would make the
        // restore read "this was saved and held nothing", which is a different
        // and wrong answer.
        if (text is not null)
            _record.Set(key, text);
    }

    public ReloadRow AddRow() => _record.AddRow();

    public bool Has(string key) => _record.Has(key);
    public string ReadString(string key, string fallback = "") => _record.GetString(key, fallback);
    public int ReadInt(string key, int fallback = 0) => _record.GetInt(key, fallback);
    public float ReadFloat(string key, float fallback = 0f) => _record.GetFloat(key, fallback);
    public bool ReadBool(string key, bool fallback = false) => _record.GetBool(key, fallback);

    public Vector ReadVector(string key, Vector fallback = default)
        => _record.Has(key) && ScriptedVarParse.Vector(_record.GetString(key), out var v) ? v : fallback;

    public EntityHandle ReadHandle(string key)
    {
        if (!_record.Has(key)
            || !uint.TryParse(_record.GetString(key), NumberStyles.Integer,
                              ReloadRecord.Culture, out uint raw))
        {
            return EntityHandle.None;
        }

        return Handles.Resolve(new EntityHandle(raw));
    }

    public T? ReadObject<T>(string key) where T : class
        => _record.Has(key) ? SaveGraph.Read(_record.GetString(key), typeof(T), _owner, key, Handles, _log) as T : null;

    public IReadOnlyList<ReloadRow> Rows => _record.Rows;

    public EntityHandle Resolve(EntityHandle handle) => Handles.Resolve(handle);
}

/// <summary>
/// AN ARBITRARY OBJECT, AS TEXT, AND THE CONTRACT FOR WHAT THAT MEANS.
///
/// <para>
/// <b>WHAT IS SUPPORTED.</b> Exhaustively, because a codec that "mostly works"
/// is one that loses a field on the class nobody tested:
/// </para>
///
/// <list type="bullet">
/// <item><c>bool</c>, every integer type, <c>float</c>, <c>double</c>,
/// <c>string</c>, any <c>enum</c>.</item>
/// <item><see cref="Vector"/>, <see cref="QAngle"/>,
/// <see cref="EntityHandle"/>.</item>
/// <item><c>T[]</c> and <c>List&lt;T&gt;</c> of any supported <c>T</c>.</item>
/// <item>A class or struct with a public parameterless constructor, whose
/// public instance fields and settable public instance properties are
/// themselves supported — recursively, to <see cref="MaxDepth"/>.</item>
/// </list>
///
/// <para>
/// <b>WHAT IS REFUSED, AND IT IS REFUSED LOUDLY.</b> A delegate, a
/// <see cref="Type"/>, an interface or abstract type with no concrete instance,
/// <c>object</c> itself, a dictionary, a multi-dimensional array, a type with no
/// public parameterless constructor, a graph deeper than
/// <see cref="MaxDepth"/>, and a cycle. Each is named in a warning and NOTHING
/// is written for it — never a silent no-op, which is <c>plan.md</c> §7's rule
/// and the difference between "this build cannot carry that" and "your state
/// vanished".
/// </para>
///
/// <para>
/// <b>A CLASS THAT CHANGED SHAPE BETWEEN THE TWO BUILDS</b> is the whole point
/// of a HOT reload, so it is not an error. The three arms are exactly
/// <c>LoadDeclaredState</c>'s, deliberately, so there is one rule in the tree
/// rather than two: a member the new build no longer declares is DROPPED and
/// named; a member that is new was never written and comes up holding its own
/// initialiser; a member whose type changed fails to parse, is named, and keeps
/// its initialiser rather than being coerced.
/// </para>
///
/// <para>
/// <b>A HANDLE INSIDE A CUSTOM OBJECT IS RE-RESOLVED ON THE WAY IN.</b> Not
/// left to the caller: the caller cannot see inside its own graph without
/// walking it, and a handle that no longer resolves still reads as the entity
/// that took its slot unless something checks. So every <see cref="EntityHandle"/> member goes through
/// <see cref="Resolve"/>, which asks the engine and answers
/// <see cref="EntityHandle.None"/> for one that names nothing.
/// </para>
///
/// <para>
/// <b>THE FORMAT</b> is one line per leaf, <c>path=value</c>, with the path
/// dotted and a collection's length under <c>path.#</c>. It is text because the
/// store is text (<see cref="ReloadRecord"/>), and it is line-oriented because
/// the alternative was a dependency on a serializer inside an assembly whose
/// whole job is to be the thing both ALCs agree on.
/// </para>
/// </summary>
public static class SaveGraph
{
    /// <summary>
    /// How deep a graph may nest.
    ///
    /// <para>
    /// A BOUND RATHER THAN NO BOUND because the writer follows references and a
    /// cycle is a stack overflow, which takes the process down rather than
    /// failing the save. Six is far past anything a piece of game state is
    /// shaped like, and a graph that needs more is one whose owner should be
    /// writing rows.
    /// </para>
    /// </summary>
    public const int MaxDepth = 6;

    /// <summary>The line the type name is written on, for diagnostics and for a refusal.</summary>
    public const string TypeKey = "$type";

    private const BindingFlags Members =
        BindingFlags.Instance | BindingFlags.Public;

    //-------------------------------------------------------------------------
    // Writing.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The object as text, or null if it was refused. A refusal has already
    /// been reported by name.
    /// </summary>
    public static string? Write(object value, string owner, string key, ILog log)
    {
        var lines = new List<string>();
        var seen = new List<object>();

        lines.Add(TypeKey + "=" + (value.GetType().FullName ?? value.GetType().Name));

        if (!WriteValue(lines, string.Empty, value, 0, seen, owner, key, log))
            return null;

        return string.Join("\n", lines);
    }

    private static bool WriteValue(List<string> lines, string path, object? value, int depth,
                                   List<object> seen, string owner, string key, ILog log)
    {
        if (value is null)
        {
            lines.Add(path + ".null=1");
            return true;
        }

        Type type = value.GetType();

        if (TryLeaf(value, type, out string? leaf))
        {
            lines.Add(path + "=" + Escape(leaf!));
            return true;
        }

        if (depth >= MaxDepth)
        {
            Refuse(owner, key, path, $"the graph is deeper than {MaxDepth} levels", log);
            return false;
        }

        if (value is IDictionary)
        {
            Refuse(owner, key, path, "a dictionary has no declared key order, so what it " +
                                     "restored as would depend on hashing rather than on the save", log);
            return false;
        }

        if (value is Delegate)
        {
            Refuse(owner, key, path, "a delegate names a METHOD of the assembly being " +
                                     "unloaded; there is nothing to write and nothing that " +
                                     "could be read back into the new build", log);
            return false;
        }

        if (value is Type)
        {
            Refuse(owner, key, path, "a Type holds its assembly, which holds the collectible " +
                                     "context -- carrying one is the leak this whole store is " +
                                     "shaped to avoid", log);
            return false;
        }

        if (type.IsArray)
        {
            if (type.GetArrayRank() != 1)
            {
                Refuse(owner, key, path, "a multi-dimensional array has no single length to " +
                                         "write; use rows or jagged arrays", log);
                return false;
            }

            var array = (Array)value;
            lines.Add(path + ".#=" + array.Length.ToString(ReloadRecord.Culture));

            for (int i = 0; i < array.Length; i++)
            {
                if (!WriteValue(lines, Index(path, i), array.GetValue(i), depth + 1, seen, owner, key, log))
                    return false;
            }

            return true;
        }

        if (value is IList list)
        {
            lines.Add(path + ".#=" + list.Count.ToString(ReloadRecord.Culture));

            for (int i = 0; i < list.Count; i++)
            {
                if (!WriteValue(lines, Index(path, i), list[i], depth + 1, seen, owner, key, log))
                    return false;
            }

            return true;
        }

        if (!type.IsValueType)
        {
            // A CYCLE, and it is refused rather than broken. Breaking one means
            // choosing which reference becomes null on the way back, and the
            // class that built the graph is the only thing that knows which.
            foreach (object already in seen)
            {
                if (ReferenceEquals(already, value))
                {
                    Refuse(owner, key, path, "the graph contains a cycle, and which reference " +
                                             "to drop is the owning class's decision rather " +
                                             "than this codec's", log);
                    return false;
                }
            }

            seen.Add(value);
        }

        if (type.IsInterface || type.IsAbstract)
        {
            Refuse(owner, key, path, $"'{type.Name}' is abstract, so the reader has no type to " +
                                     "construct", log);
            return false;
        }

        if (type.GetConstructor(Type.EmptyTypes) is null)
        {
            Refuse(owner, key, path, $"'{type.Name}' has no public parameterless constructor, so " +
                                     "the reader cannot build one to fill", log);
            return false;
        }

        foreach (var (name, memberType, read, _) in MembersOf(type))
        {
            _ = memberType;
            if (!WriteValue(lines, Join(path, name), read(value), depth + 1, seen, owner, key, log))
                return false;
        }

        return true;
    }

    //-------------------------------------------------------------------------
    // Reading.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A FRESH instance of <paramref name="target"/>, filled from
    /// <paramref name="text"/>. Null when the text is empty or the type cannot
    /// be built — and the second is named.
    /// </summary>
    public static object? Read(string text, Type target, string owner, string key, SavedHandles handles, ILog log)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string? wrote = null;

        foreach (string line in text.Split('\n'))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            string path = line.Substring(0, eq);
            string value = Unescape(line.Substring(eq + 1));

            if (path == TypeKey)
            {
                wrote = value;
                continue;
            }

            values[path] = value;
        }

        // THE TYPE NAME IS A DIAGNOSTIC AND NOT A GATE, on purpose. Across a
        // hot reload the two builds' types have the same full name and
        // different identity, which is the case this exists for; across a
        // rename they differ and the members still line up. So a mismatch is
        // REPORTED and the read goes ahead -- the per-member arms below are
        // what actually decide whether anything lands.
        string want = target.FullName ?? target.Name;
        if (wrote is not null && !string.Equals(wrote, want, StringComparison.Ordinal))
        {
            log.Warning($"[SourceSharp] {owner}: '{key}' was saved as '{wrote}' and is being " +
                           $"read into '{want}'. Members that still line up are carried; the " +
                           $"rest are dropped and named\n");
        }

        return ReadValue(values, string.Empty, target, 0, owner, key, handles, log);
    }

    private static object? ReadValue(Dictionary<string, string> values, string path, Type target,
                                     int depth, string owner, string key, SavedHandles handles, ILog log)
    {
        if (values.ContainsKey(path + ".null"))
            return null;

        Type underlying = Nullable.GetUnderlyingType(target) ?? target;

        if (IsLeafType(underlying))
        {
            if (!values.TryGetValue(path, out string? raw))
                return null;

            if (TryParseLeaf(raw, underlying, out object? parsed))
                return parsed;

            log.Warning($"[SourceSharp] {owner}: '{key}' member '{Describe(path)}' holds " +
                           $"'{raw}', which is not a {underlying.Name}. It is dropped rather " +
                           $"than coerced; the member keeps its own initialiser\n");
            return null;
        }

        if (depth >= MaxDepth)
            return null;

        if (underlying.IsArray)
        {
            Type element = underlying.GetElementType()!;
            int count = Count(values, path);
            var array = Array.CreateInstance(element, count);

            for (int i = 0; i < count; i++)
            {
                object? item = ReadValue(values, Index(path, i), element, depth + 1, owner, key, handles, log);
                if (item is not null)
                    array.SetValue(item, i);
            }

            return array;
        }

        if (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(List<>))
        {
            Type element = underlying.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(underlying)!;
            int count = Count(values, path);

            for (int i = 0; i < count; i++)
                list.Add(ReadValue(values, Index(path, i), element, depth + 1, owner, key, handles, log));

            return list;
        }

        if (underlying.IsInterface || underlying.IsAbstract
            || underlying.GetConstructor(Type.EmptyTypes) is null)
        {
            log.Warning($"[SourceSharp] {owner}: '{key}' cannot be read into " +
                           $"'{underlying.Name}': no public parameterless constructor. Nothing " +
                           $"of it is restored\n");
            return null;
        }

        object instance = Activator.CreateInstance(underlying)!;
        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, memberType, _, write) in MembersOf(underlying))
        {
            declared.Add(Join(path, name));

            object? value = ReadValue(values, Join(path, name), memberType, depth + 1, owner, key, handles, log);
            if (value is null)
                continue;

            if (memberType == typeof(EntityHandle))
                value = handles.Resolve((EntityHandle)value);

            write(instance, value);
        }

        // A MEMBER THE NEW BUILD NO LONGER DECLARES, named rather than dropped
        // in silence -- the same arm LoadDeclaredState takes for a map key a
        // build stopped declaring, and for the same reason: a value that was
        // saved and is not restored looks exactly like one that was never
        // saved.
        if (depth == 0)
            ReportDropped(values, declared, owner, key, underlying, log);

        return instance;
    }

    private static void ReportDropped(Dictionary<string, string> values, HashSet<string> declared,
                                      string owner, string key, Type target, ILog log)
    {
        string? first = null;
        int dropped = 0;

        foreach (var (path, _) in values)
        {
            // Only top-level members: a nested member's absence is already
            // implied by its parent's, and naming both would report one change
            // twice.
            int dot = path.IndexOf('.');
            string head = dot < 0 ? path : path.Substring(0, dot);

            if (head.Length == 0 || declared.Contains(head) || head == TypeKey)
                continue;

            if (first == head)
                continue;

            if (first is null)
            {
                first = head;
                dropped++;
            }
            else if (!declared.Contains(head))
            {
                dropped++;
            }
        }

        if (dropped > 0)
        {
            log.Warning($"[SourceSharp] {owner}: '{key}' carried {dropped} member(s) that " +
                           $"'{target.Name}' no longer declares, first '{first}'. They are " +
                           $"dropped rather than carried\n");
        }
    }

    //-------------------------------------------------------------------------
    // Handles.
    //-------------------------------------------------------------------------


    //-------------------------------------------------------------------------
    // Members, leaves and text.
    //-------------------------------------------------------------------------

    private static IEnumerable<(string Name, Type Type, Func<object, object?> Read,
                               Action<object, object?> Write)> MembersOf(Type type)
    {
        foreach (FieldInfo field in type.GetFields(Members))
        {
            if (field.IsInitOnly || field.IsLiteral)
                continue;

            FieldInfo f = field;
            yield return (f.Name, f.FieldType, f.GetValue, f.SetValue);
        }

        foreach (PropertyInfo property in type.GetProperties(Members))
        {
            if (!property.CanRead || !property.CanWrite
                || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            PropertyInfo p = property;
            yield return (p.Name, p.PropertyType,
                          o => p.GetValue(o), (o, v) => p.SetValue(o, v));
        }
    }

    private static bool IsLeafType(Type type)
        => type.IsEnum
        || type == typeof(bool) || type == typeof(string)
        || type == typeof(byte) || type == typeof(sbyte)
        || type == typeof(short) || type == typeof(ushort)
        || type == typeof(int) || type == typeof(uint)
        || type == typeof(long) || type == typeof(ulong)
        || type == typeof(float) || type == typeof(double)
        || type == typeof(Vector) || type == typeof(QAngle)
        || type == typeof(EntityHandle);

    /// <summary>
    /// How the MARKED tier spells a leaf, in both walks (ScriptedEntity's and
    /// SaveTiers'): the map-key spelling, except a handle, which is its raw
    /// value as this graph writes one.
    ///
    /// <para>
    /// The map-key formatter spells a handle <c>#41</c>, its entry, for a
    /// reader; neither marked parser reads that back, so until lane
    /// side-audit-2 (2026-10-02) every marked handle came back holding the new
    /// instance's initialiser. The serial is the half that matters, too: it is
    /// what the restore's validity check compares.
    /// </para>
    /// </summary>
    public static string LeafText(object value)
        => value is EntityHandle h ? h.Value.ToString(ReloadRecord.Culture) : ScriptedVarParse.Save(value);

    private static bool TryLeaf(object value, Type type, out string? text)
    {
        text = value switch
        {
            string s => s,
            bool b => b ? "1" : "0",
            Vector v => ScriptedVarParse.Save(v),
            QAngle q => string.Format(ReloadRecord.Culture, "{0} {1} {2}", q.Pitch, q.Yaw, q.Roll),
            EntityHandle h => h.Value.ToString(ReloadRecord.Culture),
            float f => f.ToString("R", ReloadRecord.Culture),
            double d => d.ToString("R", ReloadRecord.Culture),
            IFormattable n when type.IsEnum || IsInteger(type)
                => Convert.ToInt64(value, ReloadRecord.Culture).ToString(ReloadRecord.Culture),
            _ => null,
        };

        return text is not null;
    }

    private static bool IsInteger(Type type)
        => type == typeof(byte) || type == typeof(sbyte)
        || type == typeof(short) || type == typeof(ushort)
        || type == typeof(int) || type == typeof(uint)
        || type == typeof(long) || type == typeof(ulong);

    private static bool TryParseLeaf(string raw, Type type, out object? value)
    {
        value = null;

        if (type == typeof(string))
        {
            value = raw;
            return true;
        }

        if (type == typeof(bool))
        {
            if (!ScriptedVarParse.Bool(raw, out bool b))
                return false;
            value = b;
            return true;
        }

        if (type == typeof(Vector))
        {
            if (!ScriptedVarParse.Vector(raw, out var v))
                return false;
            value = v;
            return true;
        }

        if (type == typeof(QAngle))
        {
            if (!ScriptedVarParse.QAngle(raw, out var q))
                return false;
            value = q;
            return true;
        }

        if (type == typeof(EntityHandle))
        {
            if (!uint.TryParse(raw, NumberStyles.Integer, ReloadRecord.Culture, out uint h))
                return false;
            value = new EntityHandle(h);
            return true;
        }

        if (type == typeof(float))
        {
            if (!float.TryParse(raw, NumberStyles.Float, ReloadRecord.Culture, out float f))
                return false;
            value = f;
            return true;
        }

        if (type == typeof(double))
        {
            if (!double.TryParse(raw, NumberStyles.Float, ReloadRecord.Culture, out double d))
                return false;
            value = d;
            return true;
        }

        if (!long.TryParse(raw, NumberStyles.Integer, ReloadRecord.Culture, out long n))
            return false;

        try
        {
            value = type.IsEnum
                ? Enum.ToObject(type, n)
                : Convert.ChangeType(n, type, ReloadRecord.Culture);
            return true;
        }
        catch (Exception)
        {
            // OverflowException for a value that no longer fits the member's
            // type, which is exactly the "the type changed" arm. Dropped and
            // named by the caller rather than coerced.
            return false;
        }
    }

    private static int Count(Dictionary<string, string> values, string path)
        => values.TryGetValue(path + ".#", out string? raw)
           && int.TryParse(raw, NumberStyles.Integer, ReloadRecord.Culture, out int n)
           && n >= 0 && n <= 1_000_000
            ? n
            : 0;

    private static string Join(string path, string name)
        => path.Length == 0 ? name : path + "." + name;

    private static string Index(string path, int i)
        => Join(path, i.ToString(ReloadRecord.Culture));

    private static string Describe(string path) => path.Length == 0 ? "(root)" : path;

    // A value is one LINE, so a newline in it would become a second line and
    // every field after it would read from the wrong key. '=' is the separator
    // and only the FIRST one is taken, so a value may hold one freely.
    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r");

    private static string Unescape(string value)
    {
        if (value.IndexOf('\\') < 0)
            return value;

        var sb = new StringBuilder(value.Length);

        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                sb.Append(value[i]);
                continue;
            }

            char next = value[++i];
            sb.Append(next switch { 'n' => '\n', 'r' => '\r', '\\' => '\\', _ => next });
        }

        return sb.ToString();
    }

    private static void Refuse(string owner, string key, string path, string why, ILog log)
    {
        log.Warning($"[SourceSharp] {owner}: '{key}' is REFUSED at '{Describe(path)}' because " +
                       $"{why}. NOTHING of '{key}' is saved -- a class that believed it saved " +
                       $"its state and did not is the failure this refusal exists to make " +
                       $"visible\n");
    }
}
