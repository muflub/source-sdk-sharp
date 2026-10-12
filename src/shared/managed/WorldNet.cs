//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Frozen;
using System.Globalization;
using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// worldspawn's networked state, as the managed side owns it
/// (plan_gamedll_after.md Part B2).
///
/// <para>
/// TWO JOBS, one per side of the ruling. <see cref="Publish"/> tells the native
/// shim how long <see cref="WorldNetState"/>'s blob is so the shim can build
/// its table around it — that is where the props come from. <see cref="WorldNetBinding.ApplyKey"/>
/// is the "C++ calling into managed for set" half: the map's worldspawn keys
/// arrive natively, one at a time, and what each of them MEANS is decided here.
/// </para>
///
/// <para>
/// NOTHING HERE READS THE VALUES BACK OUT FOR NATIVE, and that is deliberate.
/// Native reads the blob at the offsets this declaration publishes through
/// <c>NetStateFieldOffset</c> — the mechanism that slot exists for and the one
/// <c>standalone_player_sync.cpp</c> already uses for <c>ss_player</c>'s
/// twenty-nine fields. A second route to the same bytes would be two things to
/// keep in agreement, which is TODO #5's rule.
/// </para>
/// </summary>
public static class WorldNet
{
    /// <summary>
    /// The map keys, and which declared field each of them is.
    ///
    /// <para>
    /// STOCK'S KEY NAMES, EXACTLY. Every one of these was a
    /// <c>DEFINE_KEYFIELD</c> row in <c>CWorld</c>'s datadesc or a branch of
    /// <c>CWorld::KeyValue</c>, and a map that spells one of them is a map that
    /// still works. The names on the right are the C# property names, which are
    /// not the same strings — the datadesc said <c>m_flMaxOccludeeArea</c> and
    /// the declaration says <c>MaxOccludeeArea</c> — and that is the whole
    /// reason this table exists rather than a name transformation.
    /// </para>
    ///
    /// <para>
    /// <c>maxoccludeearea_x360</c> and <c>minoccluderarea_x360</c> are NOT here.
    /// Stock's datadesc chose between the plain and the _x360 spelling with an
    /// <c>#ifdef _X360</c>, and this build has no X360 arm to compile: on this
    /// platform the pair has always been one key each, and offering the other
    /// spelling would answer a key the shipped game never read.
    /// </para>
    /// </summary>
    private enum Field
    {
        WaveHeightUnused,
        WorldMins,
        WorldMaxs,
        StartDark,
        MaxOccludeeArea,
        MinOccluderArea,
        MaxPropScreenSpaceWidth,
        MinPropScreenSpaceWidth,
        DetailSpriteMaterial,
        ColdWorld,
    }

    private static readonly FrozenDictionary<string, Field> s_Keys =
        new Dictionary<string, Field>(StringComparer.OrdinalIgnoreCase)
        {
            ["world_mins"]          = Field.WorldMins,
            ["world_maxs"]          = Field.WorldMaxs,
            ["startdark"]           = Field.StartDark,
            ["maxoccludeearea"]     = Field.MaxOccludeeArea,
            ["minoccluderarea"]     = Field.MinOccluderArea,
            ["maxpropscreenwidth"]  = Field.MaxPropScreenSpaceWidth,
            ["minpropscreenwidth"]  = Field.MinPropScreenSpaceWidth,
            ["detailmaterial"]      = Field.DetailSpriteMaterial,
            ["coldworld"]           = Field.ColdWorld,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every key worldspawn's networked state answers to, for the tests and for
    /// anybody asking what moved. The order is the dictionary's and means
    /// nothing; the SET is the fact.
    /// </summary>
    public static IReadOnlyCollection<string> Keys => s_Keys.Keys;

    /// <summary>
    /// Whether <paramref name="key"/> is one of worldspawn's networked keys.
    /// Case-insensitive, as the map parse is.
    /// </summary>
    public static bool IsKey(string key) => s_Keys.ContainsKey(key);

    /// <summary>
    /// Tells the native shim how long worldspawn's blob is, so it can build the
    /// table its props address.
    ///
    /// <para>
    /// CALLED FROM BOTH MODULES, inside the DLL-init window, because a
    /// <c>ServerClass</c> and a <c>ClientClass</c> may only be made before the
    /// engine collects the class list. The two sides pass the same number
    /// because they read the same declaration out of the same shared assembly;
    /// what differs is the C++ class each side resolves it against.
    /// </para>
    /// </summary>
    /// <returns>The payload word count native answered, or -1 on refusal.</returns>
    public static int Publish(IEngineNetworking networking, string side, INetTypeRefusals refusals, ILog log)
        => StandaloneNetStates.PublishOffHierarchy(networking, side, typeof(WorldNetState).Assembly, refusals, log);

    /// <summary>
    /// The same decision, against a state the caller already holds.
    ///
    /// <para>
    /// SPLIT OUT SO THE PARSING IS TESTABLE WITHOUT AN ENGINE, and that is not
    /// a concession: <see cref="WorldNetBinding.ApplyKey"/>'s other half is a handle resolve and
    /// a cache, and this half is every decision worldspawn's keys carry — three
    /// floats out of one string, a flag out of a number, a material name
    /// truncated to the buffer the declaration reserved. A test that had to go
    /// through the handle would be a test of the binding cache.
    /// </para>
    /// </summary>
    public static bool Apply(WorldNetState state, string key, string value, ILog log)
        => s_Keys.TryGetValue(key, out var field) && Apply(state, field, key, value, log);

    private static bool Apply(WorldNetState state, Field field, string key, string value, ILog log)
    {
        switch (field)
        {
            case Field.WorldMins:
                if (!TryParseVector(value, out var mins))
                    return Refuse(key, value, log);
                state.WorldMins = mins;
                return true;

            case Field.WorldMaxs:
                if (!TryParseVector(value, out var maxs))
                    return Refuse(key, value, log);
                state.WorldMaxs = maxs;
                return true;

            case Field.StartDark:
                state.StartDark = ParseFlag(value);
                return true;

            case Field.ColdWorld:
                state.ColdWorld = ParseFlag(value);
                return true;

            case Field.MaxOccludeeArea:
                if (!TryParseFloat(value, out float maxOccludee))
                    return Refuse(key, value, log);
                state.MaxOccludeeArea = maxOccludee;
                return true;

            case Field.MinOccluderArea:
                if (!TryParseFloat(value, out float minOccluder))
                    return Refuse(key, value, log);
                state.MinOccluderArea = minOccluder;
                return true;

            case Field.MaxPropScreenSpaceWidth:
                if (!TryParseFloat(value, out float maxProp))
                    return Refuse(key, value, log);
                state.MaxPropScreenSpaceWidth = maxProp;
                return true;

            case Field.MinPropScreenSpaceWidth:
                if (!TryParseFloat(value, out float minProp))
                    return Refuse(key, value, log);
                state.MinPropScreenSpaceWidth = minProp;
                return true;

            case Field.DetailSpriteMaterial:
                // THE ONE PLACE THIS PORT BEHAVES DIFFERENTLY FROM STOCK, and
                // it is caught rather than allowed to unwind.
                //
                // The generated setter REFUSES a value longer than the field
                // reserved, with its own argument: "a shortened path is a
                // lookup that succeeds against the wrong file or fails against
                // none, and neither says so". Stock TRUNCATED — the server sent
                // up to DT_MAX_STRING_BUFFERSIZE and the client's char[256]
                // took the first 255 bytes — so stock's outcome for an
                // over-long path was a material name that names nothing, with
                // nothing in any log.
                //
                // CAUGHT HERE AND NOT LEFT TO Entry.cs. The caller is native:
                // an exception unwinding into Source's C++ frames is what the
                // ABI's own comments refuse, and the bootstrap's catch would
                // report it as a thunk failure rather than as a map with a
                // material path too long for the wire. Recorded in
                // docs/divergences.md.
                try
                {
                    state.DetailSpriteMaterial = value;
                }
                catch (FieldRangeException e)
                {
                    log.Warning($"[SourceSharp/Server] worldspawn's 'detailmaterial' is " +
                                $"refused: {e.Message}\n");
                }
                return true;
        }

        return false;
    }

    private static bool Refuse(string key, string value, ILog log)
    {
        log.Warning($"[SourceSharp/Server] worldspawn's '{key}' is '{value}', which is not " +
                       $"a number this field can hold; left at its default\n");

        // TRUE, not false. The key IS worldspawn's — it just could not be
        // parsed — and answering false would send it on to
        // CBaseEntity::KeyValue, which would report it as an unknown key and
        // hide the real complaint behind a second, wrong one.
        return true;
    }

    /// <summary>
    /// Stock's <c>FIELD_BOOLEAN</c> parse: a number, and anything non-zero is
    /// true. Not <c>bool.TryParse</c> — a map writes "1", not "True".
    /// </summary>
    private static bool ParseFlag(string value)
        => int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out int parsed) && parsed != 0;

    private static bool TryParseFloat(string value, out float result)
        => float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                          out result);

    /// <summary>
    /// Stock's <c>sscanf( szValue, "%f %f %f", &amp;x, &amp;y, &amp;z )</c>, which
    /// is what <c>CWorld::KeyValue</c> did for world_mins and world_maxs before
    /// the parsing moved here.
    /// </summary>
    private static bool TryParseVector(string value, out Vector result)
    {
        result = default;

        string[] parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 3)
            return false;

        if (!TryParseFloat(parts[0], out float x)
         || !TryParseFloat(parts[1], out float y)
         || !TryParseFloat(parts[2], out float z))
        {
            return false;
        }

        result = new Vector(x, y, z);
        return true;
    }
}

/// <summary>
/// worldspawn's keys, applied to the state bound to the live world. See
/// <see cref="WorldNet"/> for what each key means.
/// </summary>
public interface IWorldNetBinding
{
    /// <summary>
    /// Applies one of the map's worldspawn keys. False for a key that is not
    /// the world's, or one whose value would not parse.
    /// </summary>
    bool ApplyKey(EntityHandle world, string key, string value);

    /// <summary>Drops the binding, so the next key resolves the handle again.</summary>
    void Unbind();
}

/// <summary>
/// The binding half of <see cref="WorldNet"/>, as a service: the state bound to
/// the live world, kept between the nine keys the map parse hands over in a row.
///
/// <para>
/// It was two static fields on <see cref="WorldNet"/>, which made the bound
/// world process-wide. It is one binding per server module now, registered in
/// its container and taken by <c>ServerGameDll</c>, which applies the keys and
/// drops the binding at map unload, exactly where the static call was.
/// </para>
/// </summary>
public sealed class WorldNetBinding : IWorldNetBinding
{
    private readonly Func<EntityHandle, WorldNetState> _bind;

    /// <summary>
    /// A binding with no engine behind it: every world's state is local, which
    /// is what the engine binding answered with no game running.
    /// </summary>
    public static WorldNetBinding Local(ILog log) => new(_ =>
    {
        var state = new WorldNetState();
        state.BindLocal();
        return state;
    }, log);

    /// <summary>
    /// A binding that gets its state from <paramref name="bind"/>, which may
    /// throw <see cref="BlobTooShortException"/> as the engine's does.
    ///
    /// <para>
    /// The engine-backed bind is the SERVER's (ServerModule builds it over its
    /// own engine): only the server binds a world blob, and the bind's slot is
    /// server-only. It was a <c>WorldNetBinding(ModuleEngine)</c> constructor
    /// here, which put that slot in the shared assembly (SideAuditTests, lane
    /// side-audit, 2026-10-02).
    /// </para>
    /// </summary>
    /// <param name="log">Where a world key this binding cannot apply is said (statics to zero).</param>
    public WorldNetBinding(Func<EntityHandle, WorldNetState> bind, ILog log)
    {
        _bind = bind;
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    private readonly ILog _log;

    /// <summary>The state currently bound, or null. For the tests.</summary>
    public WorldNetState? Bound => _bound;

    /// <summary>
    /// The state bound to the live world, kept between keys.
    ///
    /// <para>
    /// The map parse hands over nine keys in a row for one entity, and binding
    /// per key would resolve the handle nine times. Rebound when the handle
    /// changes, which is every level load — the world is destroyed with the rest
    /// of the entity list and made again by the next parse.
    /// </para>
    /// </summary>
    private WorldNetState? _bound;
    private EntityHandle _boundTo = EntityHandle.None;

    /// <summary>
    /// Drops the binding, so the next key resolves the handle again.
    ///
    /// <para>
    /// Called at level shutdown. Not strictly required — the handle comparison
    /// below would rebind on its own when the next world arrives with a
    /// different handle — but a bound state holds a pointer into an entity that
    /// has been freed, and a state that reports itself stale is better than one
    /// that is merely never asked.
    /// </para>
    /// </summary>
    public void Unbind()
    {
        _bound   = null;
        _boundTo = EntityHandle.None;
    }

    /// <summary>
    /// One of the map's worldspawn keys, applied to the declared state.
    ///
    /// <para>
    /// THE PARSING IS THE POINT. <c>world_mins</c> is three floats because this
    /// says so, <c>coldworld</c> is a flag because this says so, and
    /// <c>detailmaterial</c> is text truncated to the buffer the declaration
    /// reserved. None of that is on the C++ side any more, and none of it is
    /// duplicated there.
    /// </para>
    ///
    /// <para>
    /// INVARIANT CULTURE, always. A map's "-4096.5 -4096 -512" is written with
    /// a full stop wherever the server is running, and a parse that followed the
    /// machine's locale would read a comma-decimal locale's numbers as integers
    /// and put the world's floor at -4096 in a way no log would mention.
    /// </para>
    /// </summary>
    /// <returns>
    /// True when this key is one of worldspawn's networked values and was
    /// applied. False for anything else, including a value that would not parse
    /// — the shim then falls through to <c>CBaseEntity::KeyValue</c> exactly as
    /// it did for every key that was never the world's.
    /// </returns>
    public bool ApplyKey(EntityHandle world, string key, string value)
    {
        // THE NAME IS CHECKED BEFORE THE BIND, and that ordering is the whole
        // reason a key that is not the world's costs nothing: the shim offers
        // EVERY key it does not itself answer, so this runs for targetname and
        // origin and fogcolor too, and binding for them would resolve a handle
        // per key for an answer of no.
        if (!WorldNet.IsKey(key))
            return false;

        var state = Bind(world);

        if (state is null)
            return false;

        return WorldNet.Apply(state, key, value, _log);
    }

    /// <summary>
    /// The state for this world, bound once and kept.
    ///
    /// <para>
    /// Null when the entity offers no blob, which is the case a build whose
    /// native shim was not given a size is in. Reported once per binding
    /// attempt rather than swallowed: a world whose keys silently went nowhere
    /// is a level with no bounds, no occlusion parameters and no detail sprites,
    /// and every one of those is a rendering difference rather than an error.
    /// </para>
    /// </summary>
    private WorldNetState? Bind(EntityHandle world)
    {
        if (_bound is not null && _boundTo == world && _bound.IsLive)
            return _bound;

        WorldNetState state;

        try
        {
            state = _bind(world);
        }
        catch (BlobTooShortException e)
        {
            _log.Warning($"[SourceSharp/Server] worldspawn's keys have nowhere to go: {e.Message}\n");
            _bound   = null;
            _boundTo = EntityHandle.None;
            return null;
        }

        _bound   = state;
        _boundTo = world;
        return state;
    }
}
