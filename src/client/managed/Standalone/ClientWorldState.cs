using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of <c>shadow_control</c>.
///
/// <para>
/// Every class in this file has the same three lines in it, and that is the
/// point rather than an accident: a world-state controller is a networked
/// struct whose client half unpacks it and hands it to the subsystem that reads
/// it every frame. There is nothing to draw, nothing to interpolate and nothing
/// to decide — the deciding was done on the server, where the map's keys and
/// the map's inputs are.
/// </para>
///
/// <para>
/// It is not optional for exactly the reason <see cref="ClientBeam"/> is not: a
/// class with networked state of its own gets a table on a side only if that
/// side has an entity class to host it, and a server SendTable with no matching
/// RecvTable stops the join with "Client missing DT class shadow_control".
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.ShadowControl)]
public sealed class ClientShadowControl : ClientStandaloneEntity
{
    public ClientShadowControl(IEngineEntityRender render, IEngineVisibility visibility)
        : base(render)
    {
        Visibility = visibility;
    }

    /// <summary>what can be seen, and how it is lit.</summary>
    private readonly IEngineVisibility Visibility;


    public override string ClassName => EntityNames.ShadowControl;

    [NetState] public ShadowControlNetState State = new();

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        Span<float> values = stackalloc float[8]
        {
            State.Direction.X, State.Direction.Y, State.Direction.Z,
            State.Red, State.Green, State.Blue,
            State.MaxDistance,
            State.Disabled ? 1f : 0f,
        };

        Visibility.ClientSetWorldRenderState(WorldRenderState.Shadow, values);
    }
}

/// <summary>The client half of <c>water_lod_control</c>: two distances, pushed at the view renderer.</summary>
[ClientStandaloneEntity(EntityNames.WaterLodControl)]
public sealed class ClientWaterLodControl : ClientStandaloneEntity
{
    public ClientWaterLodControl(IEngineEntityRender render, IEngineVisibility visibility)
        : base(render)
    {
        Visibility = visibility;
    }

    /// <summary>what can be seen, and how it is lit.</summary>
    private readonly IEngineVisibility Visibility;


    public override string ClassName => EntityNames.WaterLodControl;

    [NetState] public WaterLodControlNetState State = new();

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        Span<float> values = stackalloc float[2] { State.CheapStart, State.CheapEnd };
        Visibility.ClientSetWorldRenderState(WorldRenderState.WaterLod, values);
    }
}

/// <summary>
/// The client half of <c>env_tonemap_controller</c>: the exposure clamps and
/// the bloom scale.
///
/// <para>
/// Only the networked half arrives here. The tonemap scale itself is a convar
/// the server writes directly, which reaches this side because a convar
/// registered by both modules under one name is one value with a reference on
/// each — so there is nothing for this class to do about it.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.EnvTonemapController)]
public sealed class ClientTonemapController : ClientStandaloneEntity
{
    public ClientTonemapController(IEngineEntityRender render, IEngineVisibility visibility)
        : base(render)
    {
        Visibility = visibility;
    }

    /// <summary>what can be seen, and how it is lit.</summary>
    private readonly IEngineVisibility Visibility;


    public override string ClassName => EntityNames.EnvTonemapController;

    [NetState] public TonemapControllerNetState State = new();

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        Span<float> values = stackalloc float[7]
        {
            State.UseCustomAutoExposureMin ? 1f : 0f,
            State.UseCustomAutoExposureMax ? 1f : 0f,
            State.UseCustomBloomScale ? 1f : 0f,
            State.CustomAutoExposureMin,
            State.CustomAutoExposureMax,
            State.CustomBloomScale,
            State.CustomBloomScaleMinimum,
        };

        Visibility.ClientSetWorldRenderState(WorldRenderState.Tonemap, values);
    }

    /// <summary>
    /// Puts the clamps back when the entity goes.
    ///
    /// <para>
    /// Stock does this in <c>~C_EnvTonemapController</c> and it is the one part
    /// of this family that is not idempotent: the globals outlive the entity,
    /// so a controller removed mid-map would otherwise leave the map clamped to
    /// whatever it last said. The numbers are left alone because the switches
    /// are what gate them.
    /// </para>
    /// </summary>
    public override void OnRemoved()
    {
        Span<float> values = stackalloc float[7] { 0f, 0f, 0f, 0f, 0f, 0f, 0f };
        Visibility.ClientSetWorldRenderState(WorldRenderState.Tonemap, values);
    }
}

/// <summary>
/// The client half of <c>env_detail_controller</c>: two fade distances, said
/// to the detail-prop system through its own convars.
///
/// <para>
/// The odd one out in this file — no subsystem push, because the stock client
/// never had one: <c>CDetailObjectSystem::LevelInitPostEntity</c> reads the
/// controller and writes <c>cl_detailfade</c> / <c>cl_detaildist</c> itself,
/// clamping the player's defaults DOWN to the map's. With the native class
/// gone that read answers nobody (shell_client_orphans.cpp) and the system
/// restores the defaults instead, every level.
/// </para>
///
/// <para>
/// The ORDER is the part that had to be measured rather than assumed. The
/// baseline entities and their blobs arrive during signon, BEFORE the client
/// gamesystems' LevelInitPostEntity — so a clamp applied on arrival was
/// watched landing seven console lines before the restore wiped it. The one
/// managed hook on the right side of the restore is <see
/// cref="ClientModule.LevelInit"/>, which fires immediately after it; that is
/// why <see cref="Reclamp"/> exists and why LevelInit calls it. OnDataChanged
/// still clamps too, for the controller that arrives mid-map, where there is
/// no restore behind it.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.EnvDetailController)]
public sealed class ClientEnvDetailController : ClientStandaloneEntity
{
    public ClientEnvDetailController(IEngineEntityRender render, IEngineConsole console)
        : base(render)
    {
        Console = console;
    }

    /// <summary>the console: convars, and commands run on the server or a client.</summary>
    private readonly IEngineConsole Console;

    public override string ClassName => EntityNames.EnvDetailController;

    [NetState] public DetailControllerNetState State = new();

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        Reclamp();
    }

    /// <summary>Says the clamp again. See the class comment for who calls this and why.</summary>
    internal void Reclamp()
    {
        Clamp("cl_detailfade", State.FadeStartDist);
        Clamp("cl_detaildist", State.FadeEndDist);
    }

    /// <summary>
    /// MIN(what is in force, what the map said) — a controller only ever
    /// brings the fade closer. Idempotent on repeat updates, because min of a
    /// min is itself, and self-healing across maps, because the detail system
    /// restores the defaults at every level init before any entity arrives.
    /// </summary>
    private void Clamp(string convar, float distance)
    {
        string? held = Console.FindConVarValue(convar);

        if (!float.TryParse(held, System.Globalization.CultureInfo.InvariantCulture, out float current))
        {
            Log.Warning($"[SourceSharp] env_detail_controller: {convar} answered " +
                        $"'{held ?? "(nothing)"}', so nothing was clamped\n");
            return;
        }

        if (distance >= current)
            return;

        // Said out loud on the ColorCorrection principle: the map ships no
        // detail props to fade, so this line IS the visible proof the blob
        // arrived and the clamp ran.
        Log.Msg($"[SourceSharp] env_detail_controller: {convar} {current} -> {distance}\n");

        Console.SetConVarValueByName(convar,
            distance.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
