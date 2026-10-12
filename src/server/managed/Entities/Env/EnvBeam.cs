using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_beam: a beam strung between two named entities.
///
/// <para>
/// Stock's has two quite different modes behind one classname. With a
/// <c>life</c> of zero it IS a beam — a persistent one, drawn for as long as it
/// is switched on. With a life above zero it STRIKES: one-shot beams fired as
/// temp entities on a timer, optionally at random points in a radius.
/// </para>
///
/// <para>
/// The persistent mode is here, and it is the one this mod could express: an
/// <c>ss_beam</c> follows the two entities and the endpoints are rewritten as
/// they move. The striking mode is not carried over — it is a temp-entity path
/// with nothing behind it on this side yet. See docs/divergences.md.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvBeam)]
public sealed class EnvBeam : ScriptedEntity
{
    public EnvBeam(IEnginePrecache precache)
    {
        Precache = precache;
    }

    /// <summary>asking for what this entity will need before the level runs.</summary>
    private readonly IEnginePrecache Precache;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => (SpawnFlag)SpawnFlags;

    /// <summary>What a map may set on this entity. See <see cref="EnvBeamVars"/>.</summary>
    [ScriptVars] public EnvBeamVars Vars = new();

    private EntityHandle _beam = EntityHandle.None;
    private SsBeam? _managedBeam;
    private bool _on;

    /// <summary>
    /// env_beam's own spawnflags.
    ///
    /// <para>
    /// A pair, and worth the type because the two are read for different purposes
    /// and the gap between 1 and 8 is not an accident: bits 2 and 4 belong to flags
    /// this port does not honour, so a reader counting upward from StartOn would
    /// guess Ring wrong.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        StartOn = 0b0000_0001,
        Ring = 0b0000_1000,
    }

    public override void OnSpawn()
    {
        if (Vars.Life != 0f)
        {
            Log.Warning($"[SourceSharp] env_beam '{Name}': a striking beam (life {Vars.Life}) " +
                            "is not carried over; it will stay lit instead\n");
        }

        if (Flags.HasFlag(SpawnFlag.Ring))
            Log.Warning($"[SourceSharp] env_beam '{Name}': ring beams are not carried over\n");

        if (Vars.Sprite.Length == 0)
        {
            Log.Warning($"[SourceSharp] env_beam '{Name}': no texture, nothing to draw\n");
            return;
        }

        int sprite = Precache.PrecacheModel(Vars.Sprite);

        if (sprite < 0)
            return;

        _beam = Entities.Create(EntityNames.SsBeam);

        if (!Entities.IsValid(_beam))
            return;

        Entities.Spawn(_beam);
        _managedBeam = FindByHandle<SsBeam>(_beam);

        if (_managedBeam is null)
            return;

        _managedBeam.SetLook(sprite, Vars.Width, Vars.Width, Vars.Noise, Vars.ScrollRate,
                             (byte)Vars.Colour.X, (byte)Vars.Colour.Y, (byte)Vars.Colour.Z, (byte)Vars.Brightness);

        if (Flags.HasFlag(SpawnFlag.StartOn))
            TurnOn();
        else
            TurnOff();
    }

    public override void OnRemoved()
    {
        if (Entities.IsValid(_beam))
            Entities.Remove(_beam);
    }

    /// <summary>
    /// The endpoints are re-read every tick rather than resolved once, which is
    /// what lets a beam follow two things that move. Stock re-resolves them per
    /// strike for the same reason.
    /// </summary>
    public override void OnThink()
    {
        if (!_on || _managedBeam is null)
            return;

        var start = Entities.FindByName(Vars.StartEntity);
        var end   = Entities.FindByName(Vars.EndEntity);

        if (Entities.IsValid(start) && Entities.IsValid(end))
        {
            _managedBeam.SetPoints(Entities.GetOrigin(start), Entities.GetOrigin(end));
            _managedBeam.Visible = true;
        }
        else
        {
            // One end missing is a beam with nowhere to go. Hidden rather than
            // drawn to the world origin, which is a line across the map.
            _managedBeam.Visible = false;
        }

        SetNextThink(0f);
    }

    private void TurnOn()
    {
        _on = true;
        SetNextThink(0f);
    }

    private void TurnOff()
    {
        _on = false;

        if (_managedBeam is not null)
            _managedBeam.Visible = false;

        StopThinking();
    }

    [ScriptedEvent("TurnOn")]
    private void OnTurnOn() { if (_managedBeam is not null) TurnOn(); }

    [ScriptedEvent("TurnOff")]
    private void OnTurnOff() { if (_managedBeam is not null) TurnOff(); }

    [ScriptedEvent("Toggle")]
    private void OnToggle()
    {
        if (_managedBeam is null) return;

        if (_on) TurnOff();
        else     TurnOn();
    }

    /// <summary>
    /// Stock strikes once. Without the striking path this lights the beam,
    /// which is the nearest honest thing it can do.
    /// </summary>
    [ScriptedEvent("StrikeOnce")]
    private void OnStrikeOnce() { if (_managedBeam is not null) TurnOn(); }
}

/// <summary>
/// The map keys <see cref="EnvBeam"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvBeamVars
{
    [VarName("LightningStart")]
    public string StartEntity { get; set; } = string.Empty;

    [VarName("LightningEnd")]
    public string EndEntity { get; set; } = string.Empty;

    /// <summary>
    /// Zero is a beam that stays. Anything else is a strike, which this does
    /// not do — the value is read only so the entity can say so.
    /// </summary>
    public float Life { get; set; }

    [VarName("texture")]
    public string Sprite { get; set; } = string.Empty;

    [VarName("BoltWidth")]
    public float Width { get; set; } = 2f;

    [VarName("NoiseAmplitude")]
    public float Noise { get; set; }

    [VarName("TextureScroll")]
    public float ScrollRate { get; set; } = 35f;

    [VarName("renderamt")]
    public int Brightness { get; set; } = 255;

    [VarName("rendercolor")]
    public Vector Colour { get; set; } = new(255f, 255f, 255f);
}
