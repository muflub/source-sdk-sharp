//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// The map keys an entity declares. Written in two places, and it is the same
/// idea at both — which is why it is one attribute rather than two names a
/// reader has to keep apart.
///
/// <para>
/// ON A PARTIAL CLASS it says the class IS a set of map keys. Every settable
/// property on it is one, keyed by its own name, holding whatever its own
/// initialiser left it holding:
/// </para>
///
/// <code>
/// [ScriptVars]
/// public partial class FuncLodVars
/// {
///     [VarName("DisappearDist")]
///     public float DisappearDistance { get; set; } = 5000f;
/// }
/// </code>
///
/// <para>
/// The generator writes the other half: the record of whether and when the map
/// wrote each key, a table the debugging views read without reflecting, and the
/// dispatch that turns a key name into an assignment. It also derives the class
/// from <see cref="ScriptedVarSet"/>, so the author writes no base — exactly as
/// <c>[StandaloneNetState]</c> does. The opt-out for something that is not a
/// key is not being settable: a value computed from two keys is a get-only
/// property, and a constant a default is written in terms of is a field.
/// </para>
///
/// <para>
/// ON A FIELD OF AN ENTITY it says this entity reads that set:
/// </para>
///
/// <code>[ScriptVars] public FuncLodVars Vars = new();</code>
///
/// <para>
/// which is <c>[NetState]</c>'s shape exactly, and for the same reason: a
/// declaration the runtime has to FIND is one somebody can leave out, so it is
/// written where the thing is rather than inferred. Leaving it out is a build
/// error, not a quiet entity — the generator reports a var set field that
/// carries no marker.
/// </para>
///
/// <para>
/// AN ENTITY MAY HOLD SEVERAL, as it may hold several net states, and here it is
/// not merely allowed but required: a class and its base each declare their own
/// keys, so TriggerVolume declares "wait" once and fifteen triggers answer to it
/// through the base's set while declaring their own beside it. Two sets on one
/// entity claiming ONE key is a build error, because a key silently reaching one
/// of two properties is undiagnosable.
/// </para>
///
/// <para>
/// AN ENTITY WITH NONE is ordinary. Most entities have no map keys at all, and
/// the absence means "no keys", never an error.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Field, Inherited = false)]
public sealed class ScriptVarsAttribute : Attribute
{
}

/// <summary>
/// The map key a property answers to, when it is not the property's own name.
///
/// <para>
/// Only needed where the two genuinely differ — <c>filtername</c> for
/// <c>FilterName</c>, <c>m_iszNewTarget</c> for <c>NewTarget</c>. Matching is
/// case-insensitive, as it must be because map data is inconsistent about case,
/// so a key that differs from the property only in capitalisation needs nothing
/// here.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class VarNameAttribute : Attribute
{
    public VarNameAttribute(string name)
    {
        Name = name;
    }

    /// <summary>Key name as it appears in the map.</summary>
    public string Name { get; }
}

/// <summary>
/// One declared key on a live var set: what it is called, what it reads into,
/// what it currently holds, and whether the map ever wrote it.
///
/// <para>
/// Emitted as a static table by the generator, with a typed accessor per entry,
/// which is why the debugging views below reflect over nothing. The accessors
/// take the set rather than closing over one, so the table is shared by every
/// instance of the class.
/// </para>
///
/// <para>
/// <c>Save</c> AND <c>Read</c> ARE NOT THE SAME FUNCTION, and the difference is
/// one type. <c>Read</c> is for a person: an enum reads as
/// <c>Bullet|0x100000 (unnamed)</c>, which is the right thing to show and is not
/// a thing any parser accepts back. <c>Save</c> is for a round trip — through
/// <see cref="ScriptedVarParse.Save"/>, which differs from
/// <see cref="ScriptedVarParse.Format"/> only in writing an enum as its number,
/// exactly as a map does. Every other type has one spelling and both functions
/// produce it.
/// </para>
///
/// <para>
/// <c>Stamp</c> exists for the hot-reload restore and for nothing else. Replaying
/// a key through <c>OnKeyDataUpdate</c> restores the VALUE and stamps it with the
/// time of the replay, so a var the map wrote at 0.0 would come back claiming it
/// was written when the reload happened — a lie about the entity's own history,
/// told quietly. The restore puts the original stamp back through this.
/// </para>
/// </summary>
public readonly record struct ScriptedVarDescriptor(
    string Name,
    string Member,
    string TypeName,
    Func<ScriptedVarSet, string> Read,
    Func<ScriptedVarSet, bool> IsSet,
    Func<ScriptedVarSet, float> LastSetAt,
    Func<ScriptedVarSet, string> Save,
    Action<ScriptedVarSet, float> Stamp);

/// <summary>
/// The base every <c>[ScriptVars]</c> class gets, and the whole of what the
/// runtime knows about one.
///
/// <para>
/// THE POINT IS <see cref="OnLoweredKey"/>. It used to be a dictionary from key
/// name to <c>FieldInfo</c>, built by reflection at registration and walked per
/// key per entity; the generated override is an ordinary C# switch on the key
/// that the compiler checked. The difference that matters is not speed — a
/// map's keys arrive once per entity — it is that the name a mapper types is
/// now matched against a set fixed at compile time, and the one thing the
/// reflective version could never do is notice a key nobody claimed.
/// </para>
/// </summary>
public abstract class ScriptedVarSet
{
    /// <summary>
    /// What <see cref="LastSetAt"/> holds before anything is written.
    ///
    /// Negative on purpose: the game clock starts at zero and only rises, so no
    /// real assignment can ever produce this, and "never" cannot be confused
    /// with "at the start of the map".
    /// </summary>
    public const float Never = -1.0f;

    /// <summary>
    /// The entity class name this set belongs to, for messages. Set by the
    /// registry when the instance is created; empty in a unit test that builds
    /// a set on its own.
    /// </summary>
    public string OwnerName { get; internal set; } = string.Empty;

    /// <summary>
    /// Assigns the map's value for <paramref name="key"/> if this set declares
    /// it, however the key is spelled. Returns whether the key was CLAIMED —
    /// which is not the same as whether it was read: a key this set declares but
    /// whose value is nonsense is still this set's key, and taking it means the
    /// message names the right entity.
    ///
    /// <para>
    /// THE OUTSIDE DOOR, and correct for any input. It folds the key and hands
    /// it to <see cref="OnLoweredKey"/>. Everything that is not the registry
    /// comes through here — tests, tools, an entity assigning its own set — and
    /// none of them has to know that the matching is done on a folded key.
    /// </para>
    ///
    /// <para>
    /// NOT VIRTUAL, deliberately. It was abstract, and the generated override
    /// matched lower-case labels, so a caller handing it "PushDir" got false and
    /// no explanation — a correctly spelled key silently refused. That is the
    /// shape of nearly every expensive bug in this port, and a precondition
    /// stated in a comment is not a precondition. One extra method makes it
    /// unreachable instead.
    /// </para>
    /// </summary>
    public bool OnKeyDataUpdate(string key, string value)
        => OnLoweredKey(LowerKey(key), value);

    /// <summary>
    /// The generated dispatch: a switch over lower-case labels.
    ///
    /// <para>
    /// THE INSIDE DOOR, and the fold has already happened. The registry calls it
    /// directly because an entity holds several sets — its own and its bases',
    /// which is how fifteen triggers read TriggerVolume's "wait" — and folding
    /// per set would fold the same key two or three times per offer. It folds
    /// once and offers the result to each.
    /// </para>
    ///
    /// <para>
    /// NO PATH REACHES BOTH DOORS. The registry calls this one through
    /// <see cref="OfferLowered"/> and never <see cref="OnKeyDataUpdate"/>;
    /// everyone else calls that one and cannot reach this, since it is
    /// protected. So the key is folded exactly once whichever way it arrives.
    /// </para>
    /// </summary>
    protected abstract bool OnLoweredKey(string key, string value);

    /// <summary>
    /// The registry's way in to <see cref="OnLoweredKey"/>.
    ///
    /// <para>
    /// A forwarder rather than making the dispatch itself reachable, because
    /// accessibility here decides what the GENERATOR has to write. Declaring the
    /// dispatch <c>protected internal</c> would let the registry call it
    /// directly — but C# then requires the override to say <c>protected</c> in
    /// an assembly that cannot see this one's internals and <c>protected
    /// internal</c> in one that can, and both kinds exist here: the server's
    /// sets are outside, the tests' sets are inside through InternalsVisibleTo.
    /// The generator would have to know which, and be wrong the day that
    /// changed. With the dispatch plainly protected it writes <c>protected
    /// override</c> always.
    /// </para>
    /// </summary>
    internal bool OfferLowered(string loweredKey, string value)
        => OnLoweredKey(loweredKey, value);

    /// <summary>
    /// A map key as <see cref="OnLoweredKey"/> wants it.
    ///
    /// <para>
    /// INVARIANT, never <c>ToLower()</c>. Turkish lowercases "I" to "ı", so a
    /// key with an I in it would stop matching on a machine set to that locale
    /// and nowhere else — a bug that cannot be reproduced by whoever is asked to
    /// look at it. The generator lowers its case labels the same way at build
    /// time, so the two agree by construction.
    /// </para>
    /// </summary>
    public static string LowerKey(string key) => key.ToLowerInvariant();

    /// <summary>Every key this set declares, in declaration order.</summary>
    public abstract IReadOnlyList<ScriptedVarDescriptor> Descriptors { get; }

    /// <summary>
    /// Puts every key back to its declared default, and forgets that the map
    /// ever wrote any of them.
    ///
    /// <para>
    /// BOTH HALVES, and the second is the one that gets missed. A set whose
    /// values are back but which still reports a key as set, with a stamp from
    /// before the reset, is lying about its own state — and it lies quietly, so
    /// what it costs is somebody's afternoon rather than a crash.
    /// </para>
    ///
    /// <para>
    /// The defaults come from a PROTOTYPE: one instance per set class, built by
    /// the ordinary constructor, whose properties this copies. That captures
    /// each initialiser exactly, whatever expression it is, with no second copy
    /// of it to drift — where re-emitting the expression into this method would
    /// run it twice and put it in two places at once. Its one hazard, sharing a
    /// mutable default between every reset, cannot arise: the only types a map
    /// value can be read into are value types and <see cref="string"/>, and the
    /// generator refuses anything else. A reference type reaching this list is
    /// where that reasoning has to be revisited.
    /// </para>
    /// </summary>
    public abstract void Reset();

    /// <summary>
    /// The clock a set stamp is taken from: GAME time, the same
    /// <c>gpGlobals-&gt;curtime</c> every entity's own logic compares against.
    ///
    /// <para>
    /// Chosen over real time and over the tick number deliberately, and the
    /// choice is not cosmetic. A timestamp is only useful next to another one,
    /// and every other time an entity holds — a think deadline, a refire delay,
    /// a respawn — is game time. Real time keeps running while the server is
    /// paused and while a level loads, so a var set during one map load and
    /// compared during the next would read as a plausible interval that never
    /// happened. The tick number would be exact but is not comparable with
    /// anything else managed code holds.
    /// </para>
    ///
    /// <para>
    /// The consequence to know about: game time RESTARTS at each level change,
    /// so a stamp is only meaningful within the map that produced it. That is
    /// true of every other deadline in the game and is why it is the right
    /// clock rather than in spite of it.
    /// </para>
    /// </summary>
    protected float Now => Clock?.Now ?? 0f;

    /// <summary>
    /// The clock <see cref="Now"/> reads: the owning registry's, bound when the
    /// instance is created (plan_abi_hotreload D8), so two instances in one
    /// process stamp their own game time. A set built on its own in a unit test
    /// has none and stamps 0, which is what an engine side that is not loaded
    /// answers too.
    /// </summary>
    internal IGameClock? Clock { get; set; }

    /// <summary>
    /// Where this set reports a value it could not read: its owner's registry
    /// log, handed in beside <see cref="Clock"/> (statics to zero). A set built
    /// on its own reports to standard error.
    /// </summary>
    internal ILog? Log { get; set; }

    /// <summary>
    /// A value this set's key could not be read from. Reported rather than
    /// assigned as nonsense, leaving the declared default in place.
    /// </summary>
    protected void ReportUnreadable(string key, string value, string type)
        => (Log ?? new StandardErrorLog()).Warning($"[SourceSharp] {(OwnerName.Length > 0 ? OwnerName : GetType().Name)}: " +
                          $"could not read '{key}' = '{value}' as {type}\n");

    /// <summary>
    /// Whether a key this set declares has been written, by name. The per-key
    /// <c>&lt;Name&gt;IsSet</c> property is what game code should use; this is
    /// for the debugging views, which are given a name and nothing else.
    /// </summary>
    public bool IsKeySet(string key)
    {
        foreach (var d in Descriptors)
        {
            if (string.Equals(d.Name, key, StringComparison.OrdinalIgnoreCase))
                return d.IsSet(this);
        }

        return false;
    }
}

/// <summary>
/// The one copy of "what a map's text means as a value".
///
/// <para>
/// Shared by the generated dispatch and by every test that checks it, which is
/// the point: if the two had their own parsers, a test comparing them would be
/// comparing the parsers rather than the thing being replaced — the mapping
/// from a KEY NAME to a member.
/// </para>
/// </summary>
public static class ScriptedVarParse
{
    private static System.Globalization.CultureInfo Culture
        => System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>A map value is text and text is always readable as itself.</summary>
    public static bool String(string value, out string result)
    {
        result = value;
        return true;
    }

    public static bool Float(string value, out float result)
        => float.TryParse(value, System.Globalization.NumberStyles.Float, Culture, out result);

    /// <summary>
    /// AN INTEGER READ AS AN INTEGER, and only then as a float.
    ///
    /// <para>
    /// THE FLOAT READING CANNOT BE FIRST, and the reason is not style. A
    /// <c>float</c> carries 24 bits of mantissa, so every value above 2^24 comes
    /// back ROUNDED: 16777217 read as 16777216, 117506305 as 117506304,
    /// 2000000001 as 2000000000. Nothing reports it -- the parse succeeds and
    /// hands back a different number. That reaches further than a map:
    /// <c>SavedEntity.Set(string, int)</c> writes the exact digits and
    /// <c>GetInt</c> reads them back through here, so a value could be SAVED
    /// perfectly and RESTORED wrong.
    /// </para>
    ///
    /// <para>
    /// THE FLOAT READING STAYS AS THE FALLBACK, because stock is not
    /// <c>int.TryParse</c> either. A FIELD_INTEGER keyvalue is parsed by
    /// <c>atoi( szValue )</c> in <c>ParseKeyvalue</c>
    /// (src/game/server/saverestore_gamedll.cpp), which truncates trailing junk
    /// rather than failing: <c>atoi("5.7")</c> is 5, and maps routinely write
    /// "5.0" where an integer is expected. <c>int.TryParse</c> refuses both, so
    /// it can only go FIRST, never alone.
    /// </para>
    ///
    /// <para>
    /// ORDERING THEM THIS WAY CHANGES NOTHING ELSE. Every string
    /// <c>int.TryParse</c> accepts, the float reading also accepted and agreed
    /// on for |value| below 2^24; above it the float reading was simply wrong.
    /// A value that OVERFLOWS int fails <c>int.TryParse</c> and lands in the
    /// same float fallback it always did -- which saturates where stock's
    /// <c>atoi</c> wraps, a divergence older than this method and pinned by
    /// <c>ScriptedVarParseIntTests</c> rather than changed here.
    /// </para>
    /// </summary>
    public static bool Int(string value, out int result)
    {
        if (int.TryParse(value, System.Globalization.NumberStyles.Integer, Culture, out result))
            return true;

        result = 0;

        if (!float.TryParse(value, System.Globalization.NumberStyles.Float, Culture, out float f))
            return false;

        result = (int)f;
        return true;
    }

    /// <summary>
    /// A map's value as an enum: the number a mapper typed, or the member's own
    /// name (plan_nettable_generic.md Step 12).
    ///
    /// <para>
    /// NUMBERS FIRST, AND THROUGH THE FLOAT-FIRST READING every other number
    /// here takes. Every existing map has numbers in it, taken from an FGD
    /// written against the numbers stock uses, and maps routinely write "5.0"
    /// where an integer is expected — <c>Enum.TryParse</c> accepts "5" and
    /// rejects "5.0", so it cannot be the first thing tried.
    /// </para>
    ///
    /// <para>
    /// THE NAME WORKS TOO, case-insensitively, because a mapper writing
    /// <c>"preciptype" "Snow"</c> is asking the obvious question and
    /// <c>Enum.TryParse</c> answers it for nothing. A comma-separated list comes
    /// with it, which is how .NET spells a flags combination.
    /// </para>
    ///
    /// <para>
    /// AN UNDEFINED NUMBER IS REFUSED FOR AN ORDINARY ENUM AND ACCEPTED FOR A
    /// FLAGS ONE, and the difference is not a compromise. For a set of
    /// alternatives, a number naming none of them is a mapper error and the
    /// caller reports it and keeps the declared default — an entity that
    /// silently does nothing is the worst failure mode. For a bitfield there is
    /// no such thing as an undefined value: a map may legitimately set a bit
    /// stock defines and this tree has not named, and refusing the word would
    /// throw away the bits the mapper did set along with the one it did not.
    /// </para>
    /// </summary>
    public static bool Enum<T>(string value, out T result) where T : struct, System.Enum
    {
        result = default;

        if (Int(value, out int number))
        {
            var parsed = (T)System.Enum.ToObject(typeof(T), number);

            if (!NetEnums.IsFlags(typeof(T)) && !System.Enum.IsDefined(typeof(T), parsed))
                return false;

            result = parsed;
            return true;
        }

        return System.Enum.TryParse(value, ignoreCase: true, out result);
    }

    /// <summary>Map booleans are 0/1, not "true"/"false".</summary>
    public static bool Bool(string value, out bool result)
    {
        result = false;

        if (!float.TryParse(value, System.Globalization.NumberStyles.Float, Culture, out float f))
            return false;

        result = f != 0.0f;
        return true;
    }

    /// <summary>
    /// "x y z", space separated, which is how a map writes every origin, axis
    /// and attachment point. Strict: a vector short of three numbers is a map
    /// error worth reporting, not a vector with an implied zero.
    /// </summary>
    public static bool Vector(string value, out Vector result)
    {
        result = Abi.Vector.Zero;

        string[] parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 3
            || !float.TryParse(parts[0], System.Globalization.NumberStyles.Float, Culture, out float x)
            || !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, Culture, out float y)
            || !float.TryParse(parts[2], System.Globalization.NumberStyles.Float, Culture, out float z))
        {
            return false;
        }

        result = new Vector(x, y, z);
        return true;
    }

    /// <summary>
    /// "pitch yaw roll", which a map writes exactly as it writes a vector.
    ///
    /// The three numbers are indistinguishable on the wire and mean something
    /// else entirely, which is the whole reason <see cref="Abi.QAngle"/> is its
    /// own type — so the two parses are separate here too, rather than one
    /// returning something the caller has to remember the meaning of.
    /// </summary>
    public static bool QAngle(string value, out QAngle result)
    {
        result = Abi.QAngle.Zero;

        if (!Vector(value, out var v))
            return false;

        result = Abi.QAngle.FromVector(v);
        return true;
    }

    /// <summary>
    /// "r g b" or "r g b a", which is how a map writes every colour. Alpha is
    /// optional and defaults to opaque, exactly as stock's colour keys do.
    /// </summary>
    public static bool Color(string value, out Color32 result)
    {
        result = default;

        string[] parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length is not (3 or 4))
            return false;

        Span<int> channels = stackalloc int[4] { 0, 0, 0, 255 };

        for (int i = 0; i < parts.Length; i++)
        {
            // Through the same float-first reading every other number takes: a
            // map writing "255.0" is writing 255, and int.TryParse is not.
            if (!Int(parts[i], out channels[i]))
                return false;
        }

        result = new Color32(channels[0], channels[1], channels[2], channels[3]);
        return true;
    }

    /// <summary>
    /// A value as text that this file can read BACK, which is what a hot reload
    /// carries a map key across in.
    ///
    /// <para>
    /// Identical to <see cref="Format"/> for every type but one. An enum formats
    /// for a person -- <c>Snowfall</c>, or <c>Bullet|0x100000 (unnamed)</c> for a
    /// flags value with a bit nothing names -- and <see cref="Enum{T}"/> reads a
    /// single member name but not a set of them and certainly not the
    /// parenthesised note. So it saves as its NUMBER, which is what a map writes
    /// and what the parse tries first, and the round trip is exact for every
    /// value of every backing type including the ones no member names.
    /// </para>
    ///
    /// <para>
    /// A separate function rather than changing <see cref="Format"/>, because
    /// the inspector's whole argument is that a field decoding to <c>3</c>
    /// instead of <c>Snowfall</c> is the readability this design keeps paying to
    /// preserve. Both spellings are wanted; they are wanted by different readers.
    /// </para>
    /// </summary>
    public static string Save(object? value) => value switch
    {
        null => "",
        System.Enum e => NetEnums.ValueOf(e).ToString(Culture),
        _ => Format(value),
    };

    /// <summary>
    /// A value as text, for the debugging views. Invariant culture throughout:
    /// this is read by a tool, and a decimal comma would be a parsing bug
    /// waiting to happen.
    /// </summary>
    public static string Format(object? value) => value switch
    {
        null => "",
        bool b => b ? "1" : "0",

        // Through the SAME formatter the networked inspector uses, so a
        // DamageType reads alike in the var view and the payload view -- and so
        // a value nothing names says so in both rather than looking like a
        // member in one of them. Enum.ToString would have printed the number
        // bare and spelled a flags set with commas.
        Enum e => NetEnums.Format(e.GetType(), NetEnums.ValueOf(e)),
        float f => f.ToString(Culture),
        Vector v => $"{v.X.ToString(Culture)} {v.Y.ToString(Culture)} {v.Z.ToString(Culture)}",
        QAngle a => $"{a.Pitch.ToString(Culture)} {a.Yaw.ToString(Culture)} {a.Roll.ToString(Culture)}",
        Color32 c => $"{c.R} {c.G} {c.B} {c.A}",
        _ => Convert.ToString(value, Culture) ?? "",
    };
}
