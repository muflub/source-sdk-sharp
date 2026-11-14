using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Shared;

namespace TF2.Descent.Server.Loot;

/// <summary>
/// <c>tf2_descent_loot</c>: one drop on the floor (plan_descent_logic WO2,
/// section 2). Sixty-four are made at level ready by <see cref="LootFloor"/>
/// and parked; none is made or removed in play. A lent entity stands where its
/// drop lies, drawn, a pickup trigger a hero walks into (Australium is taken
/// by touch) and +USEs (his seat is taken by use). A parked entity is not
/// drawn, not solid, thinks nothing, answers no use and is sent to no one
/// (<see cref="LootTransmit"/>).
///
/// <para>
/// The entity carries no seat (<see cref="LootNetState"/>): what each hero's
/// seat holds goes to his client alone, and the entity reaches only the
/// clients of the heroes who own a seat of it. Every client is sent the same
/// model, <see cref="LootLooks.ItemModel"/>; the owner's client draws its own
/// seat's (<see cref="LootLooks.ModelFor"/>).
/// </para>
/// </summary>
[ScriptedEntity(LootNetState.ClassName, ScriptedEntityKind.Item)]
[Precache(PrecacheKind.Model, LootLooks.ItemModel)]
[Precache(PrecacheKind.Model, LootLooks.AustraliumModel)]
public sealed class LootEntity(IEngineEntityRender render) : ScriptedEntity
{
    /// <summary>The trigger's half-extent: a hero walks into it to take Australium.</summary>
    public const float Half = 16f;

    [NetState] public LootNetState State = new();

    private LootFloor? _floor;

    public bool Parked { get; private set; } = true;

    /// <summary>Where it was last lent to stand.</summary>
    public Vector Position { get; private set; }

    public override bool SpawnsPhysics => false;

    public override bool TakesDamage => false;

    /// <summary>A lent drop is +USEd from the crosshair or the radius search; a parked one is not usable.</summary>
    public override UseCaps ObjectCaps => Parked ? UseCaps.None : UseCaps.ImpulseUse | UseCaps.UseInRadius;

    public override void OnSpawn()
    {
        SetModel(LootLooks.ItemModel);
        render.SetTransmitFullCheck(Handle, true);
        HideAndStill();
    }

    /// <summary>Joins the entity to its floor, once, when the pool is made.</summary>
    internal void Join(LootFloor floor) => _floor = floor;

    /// <summary>Out of the pool for drop <paramref name="drop"/>, standing at <paramref name="at"/>: drawn and a pickup.</summary>
    internal void Lend(ushort drop, Vector at)
    {
        State.Drop = drop;
        Position = at;
        Entities.SetOrigin(Handle, at);
        MakePickup(Half, Half, Half);
        SetVisible(true);
        Parked = false;
    }

    /// <summary>Back into the pool: not drawn, not solid, no drop.</summary>
    internal void Park()
    {
        State.Drop = 0;
        Parked = true;
        HideAndStill();
    }

    private void HideAndStill()
    {
        SetVisible(false);
        SetSolidity(SolidType.None, SolidFlags.None);
    }

    [ScriptedEvent("Use")]
    private void OnUsed(InputData input)
    {
        if (!Parked)
            _floor?.Use(this, input.Activator);
    }

    public override void OnTouch(EntityHandle toucher)
    {
        if (!Parked)
            _floor?.Touch(this, toucher);
    }
}
