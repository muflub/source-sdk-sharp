using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;

namespace SourceSharp.Abi;

/// <summary>
/// THE THIRD LEG OF WHAT AN ENTITY DECLARES ABOUT ITSELF.
///
/// <para>
/// Save and restore are DERIVED here, not written. A class's map keys come from
/// <c>[ScriptVar]</c>, its networked values from <c>[NetField]</c>, its wiring
/// from its outputs and the directory's connections — and
/// <c>ScriptedEntity.SaveDeclaredState</c> walks all three without the class
/// saying anything. A new entity class gets save and restore for free and
/// cannot forget, which is the whole argument for generating the net tables and
/// the layout from one declaration rather than hand-writing them.
/// </para>
///
/// <para>
/// <b>WHAT THAT LEAVES.</b> A plain managed field that is neither a map key nor
/// a networked value and still has to survive: a door's position along its
/// path, a counter's count, which of three states a puzzle is in. Before this
/// attribute the only way to carry one was to override <c>SaveState</c> and
/// hand-write a <c>Set</c>/<c>Get</c> pair — which is the shape the next person
/// omits, and omitting it is invisible: the entity comes back looking perfectly
/// ordinary with the class's declared defaults in it.
/// </para>
///
/// <para>
/// So: <c>[SaveField]</c> on the field, and it is carried. One declaration, and
/// the reader is <see cref="SaveFieldCatalog"/> — there is no second list
/// anywhere for it to drift against.
/// </para>
///
/// <para>
/// <b>FIELDS AND PROPERTIES, <c>Inherited = false</c>,</b> exactly as
/// <see cref="EntityFieldAttribute"/> and <see cref="NetFieldAttribute"/>
/// declare themselves. Not inherited because the catalog walks the whole
/// hierarchy itself (<see cref="SaveFieldCatalog"/>) and an inherited attribute
/// would make a base class's field report twice on a derived one.
/// </para>
///
/// <para>
/// <b>IT DOES NOT REPLACE THE OTHER TWO, AND MUST NOT BE PUT ON THEM.</b> A
/// value that is already a <c>[ScriptVar]</c> or a <c>[NetField]</c> is already
/// carried; marking it again would save it twice and restore it twice, in an
/// order nobody chose. The catalog refuses that combination by name rather than
/// letting it work by accident.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public class SaveFieldAttribute : Attribute
{
    /// <summary>The usual form: the member's own name is the key.</summary>
    public SaveFieldAttribute()
    {
    }

    /// <summary>
    /// For a member whose key must outlive its C# name.
    ///
    /// <para>
    /// A key is what a record is read back by, so renaming a marked field
    /// silently drops whatever the old build saved into it — the same arm
    /// <c>LoadDeclaredState</c> takes for a map key a build no longer declares,
    /// and it is reported the same way. Naming the key explicitly is how a
    /// class survives its own rename.
    /// </para>
    /// </summary>
    public SaveFieldAttribute(string key) => Key = key;

    /// <summary>The key this member is carried under, or null for its own name.</summary>
    public string? Key { get; }
}

/// <summary>
/// One marked member, resolved to something that can be read and written
/// without going back through reflection on every entity.
/// </summary>
public sealed class SaveFieldInfo
{
    private readonly FieldInfo? _field;
    private readonly PropertyInfo? _property;

    internal SaveFieldInfo(string key, FieldInfo field)
    {
        Key = key;
        _field = field;
    }

    internal SaveFieldInfo(string key, PropertyInfo property)
    {
        Key = key;
        _property = property;
    }

    // The member, whichever of the two it is: the facts below are read from it.
    private MemberInfo Member => (MemberInfo?)_field ?? _property!;

    /// <summary>What the value is carried under.</summary>
    public string Key { get; }

    /// <summary>The member's own name, for a message that has to name it.</summary>
    public string MemberName => Member.Name;

    /// <summary>The class that declared it, for the same reason.</summary>
    public string DeclaredBy => Member.DeclaringType?.Name ?? "?";

    /// <summary>Its declared type — what the value is written and read as.</summary>
    public Type MemberType => _field?.FieldType ?? _property!.PropertyType;

    /// <summary>
    /// Whether the value can be written back at all.
    ///
    /// <para>
    /// A get-only property CAN be marked and IS saved: a computed value is
    /// worth having in a <c>.sav</c> for a reader, and refusing the attribute
    /// there would make the rule "you may not observe this" rather than "this
    /// cannot come back". What is refused is the SILENCE — a restore that
    /// cannot write says so by name rather than dropping the value.
    /// </para>
    /// </summary>
    public bool CanWrite => _field is not null
                            ? !_field.IsInitOnly && !_field.IsLiteral
                            : _property is { CanWrite: true };

    public object? Read(object instance)
        => _field is not null ? _field.GetValue(instance) : _property!.GetValue(instance);

    public void Write(object instance, object? value)
    {
        if (_field is not null)
            _field.SetValue(instance, value);
        else
            _property!.SetValue(instance, value);
    }
}

/// <summary>
/// Every <see cref="SaveFieldAttribute"/> on a type, found once.
///
/// <para>
/// <b>REFLECTION AND NOT A GENERATOR, and the reason is where the attribute can
/// be put.</b> <c>ScriptedVarsGenerator</c> and <c>NetStateGenerator</c> emit
/// into a class they already own — a <c>partial</c> var set, a
/// <c>[StandaloneNetState]</c> partial — and both are shapes the class opts
/// into wholesale. <c>[SaveField]</c> goes on ANY field of ANY entity class,
/// including one that is neither partial nor generated, and a generator that
/// only worked on partial classes would be a rule that reads as an
/// implementation detail and gets forgotten. The cost is one reflection walk
/// per TYPE, cached below — not per entity and not per save.
/// </para>
///
/// <para>
/// <b>CACHED BY <see cref="Type"/>, PER INSTANCE</b> (statics to zero). A
/// dictionary keyed by a game type holds that type, which holds its assembly,
/// which holds the collectible context; so the memo is an instance the side's
/// registry owns and drops with the load it belongs to, never a static that
/// would have to forget. <see cref="Build"/> is the uncached walk, for an
/// object with no registry behind it.
/// </para>
/// </summary>
public sealed class SaveFieldCatalog
{
    private readonly ConcurrentDictionary<Type, SaveFieldInfo[]> _byType = new();

    private const BindingFlags Declared =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.DeclaredOnly;

    /// <summary>
    /// The marked members of a type, base classes first.
    ///
    /// <para>
    /// BASE FIRST AND DECLARED-ONLY PER LEVEL, because
    /// <c>Inherited = false</c> means a walk of the whole hierarchy at once
    /// would miss a base class's private fields entirely (reflection does not
    /// return them for a derived type) while returning its public ones once per
    /// level. Walking level by level returns each member exactly once, and the
    /// order is stable — which matters because a record is a dictionary and the
    /// REPORT is a count.
    /// </para>
    /// </summary>
    public SaveFieldInfo[] For(Type type)
        => _byType.GetOrAdd(type, t => Build(t, _log));

    /// <param name="log">Where a member that cannot be carried is said (statics to zero).</param>
    public SaveFieldCatalog(ILog log) => _log = log ?? throw new ArgumentNullException(nameof(log));

    private readonly ILog _log;

    /// <summary>The same walk, uncached: <see cref="For"/>'s answer for one ask.</summary>
    public static SaveFieldInfo[] Build(Type type, ILog log)
    {
        var found = new List<SaveFieldInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var chain = new List<Type>();

        for (Type? t = type; t is not null && t != typeof(object); t = t.BaseType)
            chain.Add(t);

        chain.Reverse();

        foreach (Type level in chain)
        {
            foreach (FieldInfo field in level.GetFields(Declared))
            {
                var mark = field.GetCustomAttribute<SaveFieldAttribute>(inherit: false);
                if (mark is null)
                    continue;

                // A compiler-generated backing field carries the attribute when
                // the PROPERTY carries it, and the property is walked below --
                // so taking both would save one value twice under two keys.
                if (field.Name.Length > 0 && field.Name[0] == '<')
                    continue;

                if (Refused(field.Name, level, field.GetCustomAttributes(), log))
                    continue;

                Add(found, seen, new SaveFieldInfo(mark.Key ?? field.Name, field), level, log);
            }

            foreach (PropertyInfo property in level.GetProperties(Declared))
            {
                var mark = property.GetCustomAttribute<SaveFieldAttribute>(inherit: false);
                if (mark is null)
                    continue;

                if (property.GetIndexParameters().Length != 0)
                {
                    log.Warning($"[SourceSharp] [SaveField] on {level.Name}.{property.Name} " +
                                   $"is REFUSED: an indexer has no single value to carry. " +
                                   $"Nothing of it is saved\n");
                    continue;
                }

                if (!property.CanRead)
                {
                    log.Warning($"[SourceSharp] [SaveField] on {level.Name}.{property.Name} " +
                                   $"is REFUSED: it cannot be read, so there is nothing to " +
                                   $"save. Nothing of it is saved\n");
                    continue;
                }

                if (Refused(property.Name, level, property.GetCustomAttributes(), log))
                    continue;

                Add(found, seen, new SaveFieldInfo(mark.Key ?? property.Name, property), level, log);
            }
        }

        return found.ToArray();
    }

    /// <summary>
    /// The one combination that is a mistake rather than a choice: a member that
    /// is ALREADY carried by one of the two derived tiers.
    ///
    /// <para>
    /// Saved twice and restored twice, in an order nobody chose — and the second
    /// write wins, so the symptom is a value that is right in the record and
    /// wrong in the world. Named and refused rather than left to work by
    /// accident.
    /// </para>
    /// </summary>
    private static bool Refused(string name, Type level, IEnumerable<Attribute> attributes, ILog log)
    {
        string? already = null;

        foreach (Attribute a in attributes)
        {
            already = a switch
            {
                NetFieldAttribute => "[NetField]",
                ScriptVarsAttribute => "[ScriptVars]",
                _ => already,
            };
        }

        // AND THE DECLARING CLASS, because a map key is not marked on the key.
        // [ScriptVars] goes on the var-set CLASS and every property in it is a
        // key; the per-key attribute is only VarName, and only where the name
        // differs. So a member of a var set carries no attribute of its own and
        // a check that read only the member's own list would let every map key
        // in the tree be marked and carried twice.
        already ??= level.GetCustomAttribute<ScriptVarsAttribute>(inherit: false) is not null
            ? "a [ScriptVars] set, whose every property is a map key"
            : null;

        if (already is null)
            return false;

        log.Warning($"[SourceSharp] [SaveField] on {level.Name}.{name} is REFUSED: it is " +
                       $"already carried, by {already}. Marking it again would save and " +
                       $"restore it twice in an order nobody chose, and the second write " +
                       $"wins -- a value that is right in the record and wrong in the world. " +
                       $"The derived tier still carries it\n");
        return true;
    }

    private static void Add(List<SaveFieldInfo> found, HashSet<string> seen,
                            SaveFieldInfo info, Type level, ILog log)
    {
        if (seen.Add(info.Key))
        {
            found.Add(info);
            return;
        }

        // TWO MEMBERS, ONE KEY. Whichever ran second would overwrite the first
        // on the way out and be overwritten on the way back in, so one of the
        // two would be silently dropped -- and which one depends on declaration
        // order, which is not a thing anybody decided.
        log.Warning($"[SourceSharp] [SaveField] on {level.Name}.{info.MemberName} is " +
                       $"REFUSED: '{info.Key}' is already carried by another marked member of " +
                       $"this class. Two members under one key would overwrite each other; " +
                       $"give one of them an explicit key\n");
    }

    /// <summary>How many types are remembered. For a test and for a report.</summary>
    public int CachedTypes => _byType.Count;
}
