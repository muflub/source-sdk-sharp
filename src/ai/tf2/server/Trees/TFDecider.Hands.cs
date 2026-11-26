using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The general hands (plan_bots_teams_tf2 C.3, P6 step 6; moved out of the Soldier's file for P7): fire at what is
/// in sight, a projectile with a blast at a grounded target's feet so a miss still lands its blast by them, the
/// weapon kept loaded in a lull, the shotgun when the launcher is dry up close. A class's play
/// (<see cref="ClassPlay.Engage"/>, <see cref="ClassPlay.Hands"/>) runs these unless it has its own.
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>A target moving up or down slower than this stands on something: its feet are where a blast goes.</summary>
    public const float GroundedSpeed = 60f;

    /// <summary>Feet aim reaches this far: past it the rocket's flight is long and a body shot is the better bet.</summary>
    public const float FeetReach = 1500f;

    /// <summary>The longest a gun is fired at; a launcher's rockets fly further but a person seldom tries.</summary>
    public const float FireReach = 2000f, ShotgunReach = 800f, SwapNear = 600f;

    /// <summary>A lull's reload or a weapon change, at most once in this long.</summary>
    public const float HandsEverySeconds = 1f;

    private readonly int _launcher, _shotgun;
    private Tick _handsAt;

    /// <summary>Decides on which the hands fired a rocket at a target's feet.</summary>
    public int FeetShots { get; private set; }

    /// <summary>Decides on which the hands fired at all.</summary>
    public int Shots { get; private set; }

    private int KindNamed(string name)
    {
        foreach (WeaponFacts w in _map.Weapons.All)
        {
            if (w.Name == name)
                return w.Kind;
        }

        return -1;
    }

    private int AbilityRow(string name)
    {
        for (int i = 0; i < _map.Abilities.Count; i++)
        {
            if (_map.Abilities.TryGet(i, out AbilityRow row) && row.Name == name)
                return i;
        }

        return -1;
    }

    /// <summary>The bot's own HUD past its SelfState (P7 step 2): its kit, meters and the teammates' health it shows; null in a replay.</summary>
    private Senses.TFHud? Kit => _lastHud;

    /// <summary>The HUD of the bot's last decide (its kit, meters), for the routes; null before one.</summary>
    internal Senses.TFHud? LastHud => _lastHud;

    private Senses.TFHud? _lastHud;
    private RecordedGameHud? _decodedFrom;

    /// <summary>
    /// The view's HUD as the decider reads it: the game's own TF2 HUD live; in a replay, the recording's bytes read back
    /// (format 10), once per recorded HUD; null for a view with none (an older recording).
    /// </summary>
    private Senses.TFHud? HudOf(PerceivedView view)
    {
        switch (view.GameHud)
        {
            case Senses.TFHud live:
                return live;
            case RecordedGameHud recorded when ReferenceEquals(recorded, _decodedFrom):
                return _lastHud;
            case RecordedGameHud recorded:
                _decodedFrom = recorded;
                return Senses.TFHud.FromRecorded(recorded.Bytes);
            default:
                return null;
        }
    }

    /// <summary>
    /// The weapon kind of the wielded item in <paramref name="slot"/> by the bot's own kit (P7): a Direct Hit or
    /// a Black Box as much as the stock launcher. With no kit read (a replay), a Soldier's stock weapons by name, as before P7, and
    /// none for another class: the level's table has the launcher's kind whoever carries it.
    /// </summary>
    private int KindIn(TFLoadoutSlot slot)
    {
        if (Kit is { } kit)
            return kit.TryWielded(slot, out Senses.TFKitItem item) ? item.WeaponKind : -1;
        if (_class != TFClass.Soldier)
            return -1;
        return slot switch
        {
            TFLoadoutSlot.Primary => _launcher,
            TFLoadoutSlot.Secondary => _shotgun,
            _ => -1,
        };
    }

    /// <summary>The kind of the bot's primary (its launcher, scattergun, minigun ...).</summary>
    private int PrimaryKind => KindIn(TFLoadoutSlot.Primary);

    /// <summary>The kind of the bot's secondary (its shotgun, pistol ...).</summary>
    private int SecondaryKind => KindIn(TFLoadoutSlot.Secondary);

    /// <summary>Whether the bot holds its primary: for a Soldier, its launcher, whichever it carries (the rocket jump's own read).</summary>
    private bool HoldingLauncher => PrimaryKind >= 0 && _self.ActiveWeapon == PrimaryKind;

    /// <summary>Whether the weapon in hand is the kit's melee: it is fought at reach, not at range.</summary>
    private bool HoldingMelee => Kit is { } kit && kit.TryWielded(TFLoadoutSlot.Melee, out Senses.TFKitItem m) && m.WeaponKind == _self.ActiveWeapon;

    private bool LauncherDry => _targetSighted && HoldingLauncher && _self.Clip == 0;

    private bool Busy => _now < _jumpUntil;

    private Senses.TFHud? _factsOf;
    private readonly WeaponFacts?[] _itemFacts = new WeaponFacts?[(int)TFLoadoutSlot.Count];

    /// <summary>
    /// The facts of the weapon in hand (P7): the map's for its kind, with the item's own attributes applied when the kit
    /// has it (<see cref="Knowledge.TFItemFacts.Apply"/>: a Black Box holds three, a Liberty Launcher's rockets fly 40 %
    /// faster). Worked out once per kit.
    /// </summary>
    private bool HeldFacts(out WeaponFacts own)
    {
        if (!_map.Weapons.TryGet(_self.ActiveWeapon, out own))
            return false;
        if (Kit is not { } kit)
            return true;
        if (!ReferenceEquals(kit, _factsOf))
        {
            Array.Clear(_itemFacts);
            _factsOf = kit;
        }

        foreach (Senses.TFKitItem item in kit.Kit)
        {
            if (item.WeaponKind != _self.ActiveWeapon || (uint)item.Slot >= (uint)_itemFacts.Length)
                continue;
            own = _itemFacts[(int)item.Slot] ??= Knowledge.TFItemFacts.Apply(in own, item);
            return true;
        }

        return true;
    }

    /// <summary>Whether the weapon in hand fires a rocket: a projectile that flies flat (no fall), whose blast is aimed at the floor.</summary>
    private bool Splash(out WeaponFacts own)
        => HeldFacts(out own) && own.ProjectileSpeed > 0f && own.ProjectileGravity <= 0f;

    /// <summary>A melee weapon reaches this far: the bot closes to it and swings (stock's swing trace is 48 u past the hull).</summary>
    public const float MeleeReach = 72f;

    /// <summary>
    /// The fight's range band for the weapon in hand (P7): a melee weapon's is its reach, so the bot closes and swings;
    /// any other, the launcher's (<see cref="FightNear"/> to <see cref="FightFar"/>), as before.
    /// </summary>
    private (float Near, float Far) FightBand => HoldingMelee ? (0f, MeleeReach * 0.75f) : (FightNear, FightFar);

    /// <summary>
    /// The hands on a target in sight: look at it with the bot's error, a rocket at the feet of one
    /// on the ground and ahead of it by its flight, a projectile that falls (a pipe, an arrow) lobbed on the arc that
    /// lands it there (<see cref="AimLead.Lob"/>), and hold the trigger in reach: a melee weapon's reach, a shotgun's, a
    /// gun's. Not while a rocket jump's program has the view, nor a rocket so near the blast would take the bot too.
    /// </summary>
    private void GeneralEngage()
    {
        if (!_targetSighted || _target is null || Busy)
            return;
        bool known = HeldFacts(out WeaponFacts own);
        bool splash = known && own.ProjectileSpeed > 0f && own.ProjectileGravity <= 0f;
        bool feet = splash && MathF.Abs(_targetVel.Z) < GroundedSpeed && _targetDist < FeetReach;
        var aim = new AiVector(_targetPos.X, _targetPos.Y, AimHeight(feet));
        AiVector eye = OwnEye;
        float lead = 0f;
        if (own.ProjectileSpeed > 0f)
        {
            AiVector led = AimLead.Point(eye, aim, _targetVel, own.ProjectileSpeed, LeadNow);
            // A falling projectile goes up the arc that brings it down on the led point (none when the speed cannot reach it).
            if (own.ProjectileGravity > 0f && AimLead.Lob(eye, led, own.ProjectileSpeed, own.ProjectileGravity) is { } lobbed)
                led = lobbed;
            lead = (led - aim).Length;
            aim = led;
        }

        // The error by Accuracy: a point off the body, wandering from where first sight put it.
        aim += AimError(aim, feet);
        _look = new LookIntent(LookKind.Actor, aim, _target.Id, feet ? AimPoint.Feet : AimPoint.Body, lead, 0);

        float reach = HoldingMelee ? MeleeReach : _self.ActiveWeapon == _shotgun ? ShotgunReach : FireReach;
        if (_targetDist > reach || (splash && _targetDist < SelfBlastUnits) || (own.HasClip && _self.Clip <= 0))
            return;
        _action = new ActionIntent(ActionKind.Fire, FireStyle.Spray, 0, 0);
        Shots++;
        if (feet)
            FeetShots++;
    }

    /// <summary>
    /// What the hands do beside the fire, by the bot's own kit: the secondary when the primary is dry with the target
    /// near, the dash weapon for a triple jump just ahead (TFDecider.Dash.cs), else the primary back in a lull, and a reload
    /// in a lull when the clip is not full. At most once a
    /// <see cref="ClassPlay.SwitchSeconds"/>.
    /// </summary>
    private void GeneralHands()
    {
        if (_action.Kind != ActionKind.None || Busy || _now < _handsAt)
            return;
        int primary = PrimaryKind, secondary = SecondaryKind;
        if (_targetSighted && HoldingLauncher && _self.Clip == 0 && secondary >= 0 && _targetDist < SwapNear)
        {
            Select(secondary);
            return;
        }

        // The dash weapon only in a true lull: never with a target in sight or one the bot is hunting (out of sight, still
        // believed near): the gun comes back for it, as it would with no jump near.
        if (_targetSighted || (_target is null && DashHands()))
            return;
        if (primary >= 0 && _self.ActiveWeapon != primary)
        {
            Select(primary);
            return;
        }

        if (HeldFacts(out WeaponFacts own) && own.HasClip && _self.Clip < own.ClipSize && _self.Reserve > 0
            && (_self.Flags & SelfFlags.Reloading) == 0)
        {
            _action = new ActionIntent(ActionKind.Reload, FireStyle.Tap, 0, ++_oneShot);
            _handsAt = _now + (int)(SwitchSeconds / _tick);
        }
    }

    /// <summary>The least time between two changes of the hands: the class's (<see cref="ClassPlay.SwitchSeconds"/>).</summary>
    private float SwitchSeconds => _classPlay?.SwitchSeconds ?? HandsEverySeconds;

    /// <summary>Takes out the weapon of <paramref name="kind"/>, as the HUD's weapon list selects it.</summary>
    private void Select(int kind)
    {
        _action = new ActionIntent(ActionKind.SelectWeapon, FireStyle.Tap, SourceSharp.Ai.Server.EngineBotSelf.ByKind + kind, ++_oneShot);
        _handsAt = _now + (int)(SwitchSeconds / _tick);
    }

    private bool _friendFire;

    /// <summary>
    /// The trigger held at a teammate on purpose (P7, the Medic's A4: a heal beam, a Pyro's spy-check puff): the look on
    /// <paramref name="mate"/> at <paramref name="at"/>, the trigger held, and the friend gate (TFDecider.Feel.cs) lets it
    /// go this decide. A class play calls it from its <see cref="ClassPlay.Engage"/> or <see cref="ClassPlay.OneShots"/>.
    /// Every friend-fire is a hold: the intent says so (<see cref="IntentCommand.HoldsAtFriend"/>), and a one-shot this decide
    /// (a voice line, the pop) keeps the trigger down; a decide that does not call it lets go (round 2's S9).
    /// </summary>
    private void FireAtFriend(ActorId mate, AiVector at, FireStyle style = FireStyle.Spray)
    {
        _look = new LookIntent(LookKind.Actor, at, mate, AimPoint.Body, 0f, 0);
        _action = new ActionIntent(ActionKind.Fire, style, 0, 0);
        _friendFire = true;
    }
}
