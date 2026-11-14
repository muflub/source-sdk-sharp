using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Shared;

namespace TF2.Descent.Server.Furniture;

/// <summary>
/// <c>tf2_descent_furniture</c>: one piece of furniture standing in a woken
/// room (plan_descent_logic WO3, section 2). The pool is made once at level
/// ready by <see cref="FurnitureService"/> and parked; none is made or
/// removed in play. A lent entity wears its row's TF2 model at its marker,
/// facing as the marker does: solid (a shrine, a container, the Dispenser, an
/// explosive) or not (a trap, which is stepped on). A shrine and a container
/// answer +USE; an explosive takes damage through the mode's damage rules
/// (<see cref="RunsDamageRules"/>: TF's crit and falloff reach it) until it
/// goes off. A parked entity is not drawn, not solid, takes nothing, answers
/// no use and is sent to no one (<see cref="FurnitureTransmit"/>).
///
/// <para>
/// Its engine health mirrors the row's (an explosive's health rounded up),
/// as a robot body's does, so the hurt hook's health left is the rules'.
/// </para>
/// </summary>
[ScriptedEntity(FurnitureNetState.ClassName, ScriptedEntityKind.Item)]
[Precache(PrecacheKind.Model, FurnitureCatalog.ShrineModel)]
[Precache(PrecacheKind.Model, FurnitureCatalog.ShrineSpentModel)]
[Precache(PrecacheKind.Model, FurnitureCatalog.DispenserModel)]
[Precache(PrecacheKind.Model, FurnitureCatalog.CrateModel)]
[Precache(PrecacheKind.Model, FurnitureCatalog.LockerModel)]
[Precache(PrecacheKind.Model, FurnitureCatalog.CrateOpenedModel)]
[Precache(PrecacheKind.Model, FurnitureCatalog.SarcophagusModel)]
[Precache(PrecacheKind.Model, FurnitureCatalog.SarcophagusOpenedModel)]
[Precache(PrecacheKind.Model, FurnitureCatalog.BarrelModel)]
[Precache(PrecacheKind.Model, FurnitureCatalog.PumpkinBombModel)]
[Precache(PrecacheKind.Model, FurnitureCatalog.PlateModel)]
[Precache(PrecacheKind.Model, FurnitureCatalog.ChargeModel)]
[Precache(PrecacheKind.Model, EngineFurnitureWorld.ExplosionSprite)]
[Precache(PrecacheKind.Sound, FurnitureSounds.BarrelExplode)]
[Precache(PrecacheKind.Sound, FurnitureSounds.PumpkinExplode)]
[Precache(PrecacheKind.Sound, FurnitureSounds.ShrineUse)]
[Precache(PrecacheKind.Sound, FurnitureSounds.ContainerOpen)]
[Precache(PrecacheKind.Sound, FurnitureSounds.DispenserHeal)]
[Precache(PrecacheKind.Sound, FurnitureSounds.DartTell)]
[Precache(PrecacheKind.Sound, FurnitureSounds.DartFire)]
[Precache(PrecacheKind.Sound, FurnitureSounds.FlameTell)]
[Precache(PrecacheKind.Sound, FurnitureSounds.FlameFire)]
[Precache(PrecacheKind.Sound, FurnitureSounds.ChargeTell)]
[Precache(PrecacheKind.Sound, FurnitureSounds.ChargeFire)]
[Precache(PrecacheKind.Sound, FurnitureSounds.TeslaTell)]
[Precache(PrecacheKind.Sound, FurnitureSounds.TeslaFire)]
public sealed class FurnitureEntity(IEngineEntityRender render, IServerSkins skins) : ScriptedEntity
{
    [NetState] public FurnitureNetState State = new();

    private FurnitureService? _service;
    private string _model = "";
    private int _health;
    private Vector _at;

    public bool Parked { get; private set; } = true;

    /// <summary>The row the entity is lent to; -1 while parked.</summary>
    public int Row { get; private set; } = -1;

    /// <summary>Whether it takes damage now: a lent explosive that has not gone off.</summary>
    public bool Breakable { get; private set; }

    /// <summary>Whether +USE reaches it now: a lent shrine or container.</summary>
    public bool Usable { get; private set; }

    public string Model => _model;

    /// <summary>The engine health the entity holds: a lent explosive's health rounded up, else 0.</summary>
    public int EngineHealth => _health;

    public override bool SpawnsPhysics => false;

    public override bool TouchEveryTick => false;

    public override bool RunsDamageRules => true;

    public override bool TakesDamage => !Parked && Breakable;

    public override UseCaps ObjectCaps => !Parked && Usable ? UseCaps.ImpulseUse | UseCaps.UseInRadius : UseCaps.None;

    public override void OnSpawn()
    {
        SetModel(FurnitureCatalog.CrateModel);
        _model = FurnitureCatalog.CrateModel;
        render.SetTransmitFullCheck(Handle, true);
        HideAndStill();
    }

    /// <summary>Joins the entity to its service, once, when the pool is made.</summary>
    internal void Join(FurnitureService service) => _service = service;

    /// <summary>Out of the pool for row <paramref name="row"/>: at its place and facing, in its look.</summary>
    internal void Lend(int row, Vector at, float yaw, FurnitureLook look)
    {
        Row = row;
        _at = at;
        Entities.SetAngles(Handle, new Vector(0f, yaw, 0f));
        Show(look);
        SetVisible(true);
        Parked = false;
    }

    /// <summary>Its look again (a shrine spent, a container opened, a Dispenser emptied): model, skin, solidity, health, net state.</summary>
    internal void Show(FurnitureLook look)
    {
        if (look.Model != _model)
        {
            SetModel(look.Model);
            _model = look.Model;
        }

        Entities.SetOrigin(Handle, _at + new Vector(0f, 0f, look.Lift));

        skins.SetSkin(Handle, look.Skin);
        if (look.Solid)
        {
            Entities.SetBounds(Handle, look.Mins, look.Maxs);
            SetSolidity(SolidType.BoundingBox, SolidFlags.None);
        }
        else
        {
            SetSolidity(SolidType.None, SolidFlags.None);
        }

        Breakable = look.Health > 0;
        Usable = look.Usable;
        _health = Math.Max(0, look.Health);
        Health = _health;
        State.Entry = look.Entry;
        State.Flags = (byte)look.Flags;
        State.Left = look.Left;
    }

    /// <summary>Back into the pool: not drawn, not solid, no row.</summary>
    internal void Park()
    {
        Parked = true;
        Row = -1;
        Breakable = false;
        Usable = false;
        _health = 0;
        State.Entry = 0;
        State.Flags = 0;
        State.Left = 0;
        HideAndStill();
    }

    private void HideAndStill()
    {
        SetVisible(false);
        SetSolidity(SolidType.None, SolidFlags.None);
    }

    /// <summary>The engine health after a hit the entity answered: the shell subtracts the answer itself.</summary>
    internal void Took(int answered) => _health = Math.Max(0, _health - answered);

    [ScriptedEvent("Use")]
    private void OnUsed(InputData input)
    {
        if (!Parked && Usable)
            _service?.Use(this, input.Activator);
    }

    public override int OnDamagedAt(EntityHandle attacker, float damage, DamageType damageType,
                                    Vector position, Vector force, HitGroup hitGroup = HitGroup.Generic)
        => _service is { } service && !Parked && Breakable ? service.Hurt(this, attacker, damage) : 0;
}

/// <summary>
/// How a row looks on its entity: the catalog entry, the model and skin, how
/// high above the marker its origin stands (a model whose origin is at its
/// middle is lifted to stand on the floor), whether it is solid and its box,
/// whether +USE reaches it, the health it takes damage with (0: none), and the
/// net state's flags and what is left.
/// </summary>
public readonly record struct FurnitureLook(byte Entry, string Model, int Skin, float Lift, bool Solid, Vector Mins, Vector Maxs,
                                            bool Usable, int Health, FurnitureFlags Flags, byte Left);

/// <summary>The furniture's sounds, TF2's script sounds (an explosion's name says Explo, so the horde's ear hears it as one).</summary>
public static class FurnitureSounds
{
    public const string BarrelExplode = "BaseExplosionEffect.Sound";
    public const string PumpkinExplode = "Halloween.PumpkinExplode";
    public const string ShrineUse = "Halloween.spell_pickup";
    public const string ContainerOpen = "AmmoPack.Touch";
    public const string DispenserHeal = "Building_Dispenser.Heal";
    public const string DartTell = "Halloween.ClockTick";
    public const string DartFire = "Weapon_Pistol.Single";
    public const string FlameTell = "Weapon_FlameThrower.PilotLoop";
    public const string FlameFire = "Weapon_FlameThrower.FireStart";
    public const string ChargeTell = "Halloween.spelltick_a";
    public const string ChargeFire = "Weapon_Grenade_Det_Pack.Explode";
    public const string TeslaTell = "DoSpark";
    public const string TeslaFire = "Halloween.spell_lightning_impact";

    /// <summary>A trap's tell, given when it is tripped.</summary>
    public static string TellOf(string trap) => trap switch
    {
        "dart" => DartTell,
        "flame_jet" => FlameTell,
        "charge" => ChargeTell,
        _ => TeslaTell,
    };

    /// <summary>A trap's sound when it fires.</summary>
    public static string FireOf(string trap) => trap switch
    {
        "dart" => DartFire,
        "flame_jet" => FlameFire,
        "charge" => ChargeFire,
        _ => TeslaFire,
    };

    public static string ExplodeOf(string explosive) => explosive == "pumpkin_bomb" ? PumpkinExplode : BarrelExplode;
}

/// <summary>
/// Which furniture entities are parked: a table by edict index holding the
/// parked entity's handle, so the transmit rule's question costs one array
/// read.
/// </summary>
[SideService(Side.Server)]
public sealed class FurnitureParking
{
    private readonly uint[] _parked = new uint[EngineLimits.MaxEdicts];

    public void Park(EntityHandle entity)
    {
        int i = entity.EntryIndex;
        if ((uint)i < (uint)_parked.Length)
            _parked[i] = entity.Value;
    }

    public void Unpark(EntityHandle entity)
    {
        int i = entity.EntryIndex;
        if ((uint)i < (uint)_parked.Length && _parked[i] == entity.Value)
            _parked[i] = 0;
    }

    public bool IsParked(EntityHandle entity)
    {
        int i = entity.EntryIndex;
        return (uint)i < (uint)_parked.Length && entity.Value != 0 && _parked[i] == entity.Value;
    }

    public void Clear() => Array.Clear(_parked);
}

/// <summary>
/// A parked furniture entity is never sent; a lent one is left to the
/// ordinary check (every client sees furniture alike). Registered plain:
/// naming <see cref="ITransmitRule"/> in <c>As</c> registers it twice (HO0's trap).
/// </summary>
[SideService(Side.Server)]
public sealed class FurnitureTransmit(FurnitureParking parking) : ITransmitRule, IOrderedHook
{
    /// <summary>In TF2 Descent's furniture block, beside the horde's (First + 500) and the floor's (+810).</summary>
    public const int Order = ModStages.First + 853;

    public int HookOrder => Order;

    public TransmitAnswer ShouldTransmit(EntityHandle entity, int recipientSlot) =>
        parking.IsParked(entity) ? TransmitAnswer.Never : TransmitAnswer.Default;
}
