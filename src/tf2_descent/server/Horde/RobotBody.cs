using SourceSharp.Abi;
using SourceSharp.Ai;

using TF2.Descent.Ai.Bodies;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.Horde;

/// <summary>
/// <c>tf2_descent_robot</c>: one pooled robot body (plan_descent_logic L1, 3.2,
/// HO2). Forty-eight and the boss's are made once at level ready and parked;
/// none is made or removed afterwards. A lent body wears its robot's model at
/// its rolled scale, with the hull scaled to match (<c>SetBounds</c>: the
/// hitboxes follow the model's scale by themselves, the hull does not; HO0
/// row 1), is solid, and runs the mode's damage rules
/// (<see cref="RunsDamageRules"/>: TF's crit, falloff and blast push reach
/// <see cref="OnDamagedAt"/>). A parked body is invisible, not solid, refuses
/// every hit and is never sent (<see cref="HordeTransmit"/>).
///
/// <para>
/// The body keeps its own place (<see cref="Position"/>, <see cref="Yaw"/>):
/// nothing but the horde moves it, so the place it was last told is where it
/// is, and handing the robot's state back reads no engine. Its engine health
/// is kept at the robot's life rounded up, so the hurt hook's health left is
/// the roster's (plan 8.2); the shell subtracts what <see cref="OnDamagedAt"/>
/// answers, so the body mirrors the number rather than reading it.
/// </para>
/// </summary>
[ScriptedEntity(RobotNetState.ClassName, ScriptedEntityKind.Item)]
[Precache(PrecacheKind.Model, RobotLooks.ScoutModel)]
[Precache(PrecacheKind.Model, RobotLooks.SoldierModel)]
[Precache(PrecacheKind.Model, RobotLooks.PyroModel)]
[Precache(PrecacheKind.Model, RobotLooks.DemomanModel)]
[Precache(PrecacheKind.Model, RobotLooks.HeavyModel)]
[Precache(PrecacheKind.Model, RobotLooks.EngineerModel)]
[Precache(PrecacheKind.Model, RobotLooks.MedicModel)]
[Precache(PrecacheKind.Model, RobotLooks.SniperModel)]
[Precache(PrecacheKind.Model, RobotLooks.SpyModel)]
[Precache(PrecacheKind.Model, RobotLooks.GiantScoutModel)]
[Precache(PrecacheKind.Model, RobotLooks.GiantSoldierModel)]
[Precache(PrecacheKind.Model, RobotLooks.GiantPyroModel)]
[Precache(PrecacheKind.Model, RobotLooks.GiantDemomanModel)]
[Precache(PrecacheKind.Model, RobotLooks.GiantHeavyModel)]
public sealed class RobotBody(IEngineEntityRender render) : ScriptedEntity
{
    [NetState] public RobotNetState State = new();

    private RobotBodies? _pool;
    private string _model = "";
    private int _health;

    /// <summary>The body's index in the pool, 1 to the pool's last body (the AI session's slot).</summary>
    public int Index { get; private set; }

    /// <summary>The body's weapon entity, made with it.</summary>
    public RobotWeapon? Weapon { get; private set; }

    public bool Parked { get; private set; } = true;

    /// <summary>The current lend; not valid while parked.</summary>
    public BodyLease Lease { get; private set; }

    /// <summary>The robot holding the body; none while parked.</summary>
    public RobotId Robot { get; private set; }

    public AiVector Position { get; private set; }

    public float Yaw { get; private set; }

    public float Scale { get; private set; } = 1f;

    /// <summary>The clip the robot started with: -1, a full one (no robot fires yet).</summary>
    public int Clip { get; private set; } = -1;

    /// <summary>The engine health the body holds: the robot's life rounded up, 0 when spent.</summary>
    public int EngineHealth => _health;

    public string Model => _model;

    /// <summary>Whether the body runs now (the run sequence), else stands.</summary>
    public bool Running { get; private set; }

    public override bool RunsDamageRules => true;

    public override bool TakesDamage => !Parked;

    public override bool TouchEveryTick => false;

    public override bool SpawnsPhysics => false;

    public override void OnSpawn()
    {
        SetModel(RobotLooks.SoldierModel);
        _model = RobotLooks.SoldierModel;
        render.SetTransmitFullCheck(Handle, true);
        SetVisible(false);
        SetSolidity(SolidType.None, SolidFlags.None);
    }

    /// <summary>Joins the body to its pool, its index and its weapon, once, when the pool is made.</summary>
    internal void Join(RobotBodies pool, int index, RobotWeapon weapon)
    {
        _pool = pool;
        Index = index;
        Weapon = weapon;
    }

    /// <summary>
    /// Out of the pool for <paramref name="robot"/>: its model and scale, the
    /// hull scaled, at its place and facing, its life as health, solid and
    /// drawn, its net state filled.
    /// </summary>
    internal void Lend(BodyLease lease, RobotId robot, string model, float scale, AiVector at, float yaw, double life,
                       double lifeMax, byte template, byte level, RobotFlags flags, byte statuses)
    {
        Lease = lease;
        Robot = robot;
        Clip = -1;
        if (model != _model)
        {
            SetModel(model);
            _model = model;
        }

        Scale = scale;
        render.SetModelScale(Handle, scale);
        Entities.SetBounds(Handle, RobotHull.Mins * scale, RobotHull.Maxs * scale);
        MoveTo(at, yaw);
        _health = Math.Max(1, (int)Math.Ceiling(life));
        Health = _health;
        SetSolidity(SolidType.BoundingBox, SolidFlags.None);
        SetVisible(true);
        Parked = false;
        Running = false;

        State.ModelScale = scale;
        State.Template = template;
        State.Level = level;
        State.Flags = (byte)(flags | RobotFlags.Lent);
        State.Statuses = statuses;
        State.MoveX = 0f;
        State.MoveY = 0f;
        State.BodyPitch = 0f;
        State.BodyYaw = 0f;
        SetLife(life, lifeMax);
    }

    /// <summary>Back into the pool: not drawn, not solid, no robot.</summary>
    internal void Park()
    {
        SetSolidity(SolidType.None, SolidFlags.None);
        SetVisible(false);
        Parked = true;
        Lease = default;
        Robot = RobotId.None;
        Running = false;
        State.Flags = 0;
        State.Template = 0;
    }

    /// <summary>Puts the body at <paramref name="at"/>, facing <paramref name="yaw"/> degrees.</summary>
    public void MoveTo(AiVector at, float yaw)
    {
        Position = at;
        Yaw = yaw;
        Entities.SetOrigin(Handle, HordeVectors.ToEngine(at));
        Entities.SetAngles(Handle, new Vector(0f, yaw, 0f));
    }

    /// <summary>Plays <paramref name="sequence"/> from its start, on the server's skeleton and, through the net state, the clients'.</summary>
    internal void Play(int sequence, bool running)
    {
        Running = running;
        if (sequence < 0)
            return;
        render.PlaySequence(Handle, sequence, 1f, true);
        State.Sequence = (short)sequence;
        State.SequenceSerial++;
        State.MoveX = running ? 1f : 0f;
    }

    /// <summary>The health bar's fraction, 0 to 255.</summary>
    internal void SetLife(double life, double lifeMax) =>
        State.Life = (byte)Math.Clamp((int)Math.Ceiling(255 * (lifeMax > 0 ? life / lifeMax : 0)), 0, 255);

    /// <summary>The robot died: it stops blocking at once; the pool parks it at the next decision.</summary>
    internal void Died()
    {
        _health = 0;
        State.Flags = (byte)((RobotFlags)State.Flags | RobotFlags.Dead);
        State.Life = 0;
        SetSolidity(SolidType.None, SolidFlags.None);
    }

    internal void SetStatuses(byte statuses) => State.Statuses = statuses;

    /// <summary>The engine health after a hit the body answered: the shell subtracts the answer itself.</summary>
    internal void Took(int answered) => _health = Math.Max(0, _health - answered);

    public override int OnDamagedAt(EntityHandle attacker, float damage, DamageType damageType,
                                    Vector position, Vector force, HitGroup hitGroup = HitGroup.Generic)
        => _pool is { } pool && !Parked ? pool.Hit(this, attacker, damage, damageType, position, force, hitGroup) : 0;
}

/// <summary>
/// <c>tf2_descent_robot_weapon</c>: what a robot holds, its own entity (one
/// edict; plan section 2, CR2's ruling), made once with its body, parented to
/// it on the server and bone-merged to it on the client. Hidden and never sent
/// while its body is parked.
/// </summary>
[ScriptedEntity(RobotWeaponNetState.ClassName, ScriptedEntityKind.Effect)]
[Precache(PrecacheKind.Model, RobotLooks.RocketLauncher)]
[Precache(PrecacheKind.Model, RobotLooks.GrenadeLauncher)]
[Precache(PrecacheKind.Model, RobotLooks.Flamethrower)]
[Precache(PrecacheKind.Model, RobotLooks.Scattergun)]
[Precache(PrecacheKind.Model, RobotLooks.Minigun)]
[Precache(PrecacheKind.Model, RobotLooks.Shotgun)]
[Precache(PrecacheKind.Model, RobotLooks.SyringeGun)]
[Precache(PrecacheKind.Model, RobotLooks.SniperRifle)]
[Precache(PrecacheKind.Model, RobotLooks.Knife)]
public sealed class RobotWeapon(IEngineEntityRender render) : ScriptedEntity
{
    [NetState] public RobotWeaponNetState State = new();

    private string _model = "";

    public bool Shown { get; private set; }

    public string Model => _model;

    public override bool TouchEveryTick => false;

    public override bool SpawnsPhysics => false;

    public override void OnSpawn()
    {
        render.SetTransmitFullCheck(Handle, true);
        SetVisible(false);
    }

    /// <summary>Follows <paramref name="body"/> for the level: parented here, bone-merged on the client by its index.</summary>
    internal void Attach(RobotBody body)
    {
        Entities.SetParent(Handle, body.Handle);
        State.OwnerIndex = (short)Entities.GetIndex(body.Handle);
    }

    /// <summary>Drawn with <paramref name="model"/>; a null model (bare fists) stays hidden.</summary>
    internal void Show(string? model)
    {
        if (model is null)
        {
            Hide();
            return;
        }

        if (model != _model)
        {
            SetModel(model);
            _model = model;
        }

        SetVisible(true);
        Shown = true;
    }

    internal void Hide()
    {
        SetVisible(false);
        Shown = false;
    }
}
