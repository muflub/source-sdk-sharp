using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_wind: the map's weather, as eleven numbers and a seed.
///
/// <para>
/// Stock's server half decides almost nothing: it copies the mapper's keys
/// into <c>CEnvWindShared</c>, seeds it, stamps the start time, and from then
/// on BOTH sides run the same deterministic gust simulation — the client from
/// the same numbers, so no wind speed is ever networked, only the parameters
/// that generate all of them. This port keeps exactly that split: the keys and
/// the seed travel in <see cref="EnvWindNetState"/>, and the simulation is the
/// stock <c>CEnvWindShared</c> kept alive client-side by
/// <c>shell_effects_sim.cpp</c>, where every consumer of
/// <c>GetWindspeedAtTime</c> (the ropes' sway, the tree sprites) already
/// lives.
/// </para>
///
/// <para>
/// Not carried over, recorded in docs/divergences.md: the OnGustStart /
/// OnGustEnd outputs (stock fires them from a server-side copy of the same
/// simulation; neither shipped map wires either), and the commented-out
/// gustsound stock itself never finished.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvWind, ScriptedEntityKind.Effect)]
public sealed class EnvWind : ScriptedEntity
{
    public EnvWind(IEngineEntities entities, IGameClock clock)
    {
        EntityApi = entities;
        Clock = clock;
    }

    /// <summary>reading this entity's own spawn facts back.</summary>
    private readonly IEngineEntities EntityApi;
    /// <summary>the server's clock, for the simulation's epoch.</summary>
    private readonly IGameClock Clock;

    /// <summary>What a map may set on this entity. See <see cref="EnvWindVars"/>.</summary>
    [ScriptVars] public EnvWindVars Vars = new();

    [NetState] public EnvWindNetState State = new();

    public override void OnSpawn()
    {
        State.MinWind       = Vars.MinWind;
        State.MaxWind       = Vars.MaxWind;
        State.MinGust       = Vars.MinGust;
        State.MaxGust       = Vars.MaxGust;
        State.MinGustDelay  = Vars.MinGustDelay;
        State.MaxGustDelay  = Vars.MaxGustDelay;
        State.GustDirChange = Vars.GustDirChange;
        State.GustDuration  = Vars.GustDuration;

        // Stock passes literally 0: m_EnvWindShared.Init( entindex(), 0, ... )
        // — the wind has never actually been seeded randomly, and both sides'
        // streams run from zero. Kept, exactly; a rolled seed here would be a
        // different wind from stock's for the same map.
        State.WindSeed = 0;

        // Where the wind blows from is the entity's facing, and it is the one
        // spawn fact not in the keys.
        State.InitialWindDir = NormaliseYaw(EntityApi.GetAngles(Handle).Y);

        State.InitialWindSpeed = 0f;
        State.StartTime = Clock.Now;
    }

    /// <summary>
    /// A yaw as the wire wants it: nine unsigned bits, 0–359. A mapper's −90
    /// and 270 are the same facing; stock's field is SPROP_UNSIGNED and gets
    /// this for free by truncation, which is the wrong kind of free.
    /// </summary>
    internal static int NormaliseYaw(float yaw)
    {
        int deg = (int)MathF.Round(yaw) % 360;
        return deg < 0 ? deg + 360 : deg;
    }
}

/// <summary>
/// The map keys <see cref="EnvWind"/> declares. All default to ZERO, which is
/// stock's real default: DEFINE_KEYFIELD writes nothing when the key is
/// absent and a native entity's memory is zeroed at allocation. The friendly
/// numbers a mapper sees are Hammer's FGD defaults, which live in the editor
/// and never in the entity.
/// </summary>
[ScriptVars]
public partial class EnvWindVars
{
    [VarName("minwind")]       public int MinWind { get; set; }
    [VarName("maxwind")]       public int MaxWind { get; set; }
    [VarName("mingust")]       public int MinGust { get; set; }
    [VarName("maxgust")]       public int MaxGust { get; set; }
    [VarName("mingustdelay")]  public float MinGustDelay { get; set; }
    [VarName("maxgustdelay")]  public float MaxGustDelay { get; set; }
    [VarName("gustdirchange")] public int GustDirChange { get; set; }
    [VarName("gustduration")]  public float GustDuration { get; set; }
}
