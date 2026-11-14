//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Combat;
using TF2.Server.Entities.Items;
using TF2.Server.Medigun;
using TF2.Server.Projectiles;
using TF2.Server.Weapons;

using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;

namespace TF2.Server.Player;

/// <summary>
/// One taunt's timed attack: the keying of <c>CTFPlayer::Taunt</c> on the
/// scene the response rules answered (tf_player.cpp:17979-18068 — pyro
/// <c>taunt02</c> hadouken at 2.1 s, heavy <c>taunt03_v1</c> high noon at
/// 1.8 s, any spy <c>taunt03</c> fencing slash at 1.8 s), the PostThink
/// window that fires it (:2691-2695), and <c>DoTauntAttack</c>'s two arms —
/// the box arm's volume, visibility test, damage and -45° launch
/// (:18387-18451); the high noon's trace and team gate (:18613-18635). The
/// fencing chain re-arms itself, whoever stood in the box: slash B 0.47 s
/// after the slash A, the stab 1.73 s later still (:18436-18445).
///
/// <para>
/// Each hit goes out through the damage layer under a scope naming the
/// custom (<c>TF_DMG_CUSTOM_TAUNTATK_*</c>) and the held weapon — the
/// inflictor and weapon of stock's <c>CTakeDamageInfo</c>, whose caller's
/// bits stand because the layer keeps a scoped hit's.
/// </para>
/// </summary>
public sealed class TFTauntAttack(TFPlayerCarriers carriers, ITFMapPlayers players, IEngineEntities entities,
                                  IEngineWorldQuery world, IEngineDamage damage, IEnginePlayers enginePlayers,
                                  IEngineNetworking networking, TFWeaponScripts scripts, TFDamageScopes scopes,
                                  IEntityDirectory directory, ITFLunchboxEffects lunchbox,
                                  ITFWeaponOwners owners, AttributeHooks hooks,
                                  IUniformRandom random, IEngineGameEvents events,
                                  IEngineEffects effects, IEnginePrecache precache, TFBlastApplier blast,
                                  ITempEnts temps, ITFMedigunWeapons mediguns, ITFPlayerStats stats,
                                  ITFProjectileEffects projectile)
    : IPlayerComponent
{
    /// <summary>After the taunt think (26): the same PostThink, stock's stop asked before the window fires.</summary>
    public int Order => 27;

    /// <summary>The pyro hadouken's scene, whose attack lands 2.1 s in (:17982-17986).</summary>
    public const string PyroHadoukenScene = "scenes/player/pyro/low/taunt02.vcd";

    /// <summary>The Rainblower's bubbles scene, the armageddon round 3 s in (:17987-17990).</summary>
    public const string PyroArmageddonScene = "scenes/player/pyro/low/taunt_bubbles.vcd";

    /// <summary>The Scorch Shot's own scene, the flare launch 1.9 s in (:18012-18015).</summary>
    public const string PyroScorchShotScene = "scenes/player/pyro/low/taunt_scorch_shot.vcd";

    /// <summary>The plain pyro taunt's scene head, the Phlogistinator's rage
    /// arm's gate (<c>StringHasPrefix(szResponse, "scenes/player/pyro/low/
    /// taunt01")</c>, tf_player.cpp:17901): the special taunts' taunt02 and
    /// later scenes do not light it.</summary>
    public const string PyroRageScenePrefix = "scenes/player/pyro/low/taunt01";

    /// <summary>The heavy high noon's scene, 1.8 s in (:18020-18024).</summary>
    public const string HeavyHighNoonScene = "scenes/player/heavy/low/taunt03_v1.vcd";

    /// <summary>The Eyelander's barbarian swing scene, 2.55 s in (:18093-18100).</summary>
    public const string DemomanBarbarianScene = "scenes/player/demoman/low/taunt09.vcd";

    /// <summary>The Sandman's grand slam scene, 4.03 s in (:18036-18042).</summary>
    public const string ScoutGrandSlamScene = "scenes/player/scout/low/taunt05_v1.vcd";

    /// <summary>The Huntsman's arrow stab scene, the impale 0.85 s in (:18070-18076).</summary>
    public const string SniperArrowScene = "scenes/player/sniper/low/taunt04.vcd";

    /// <summary>The Equalizer's grenade kill scene, the blast 3.5 s in (:18078-18091).</summary>
    public const string SoldierGrenadeScene = "scenes/player/soldier/low/taunt05.vcd";

    /// <summary>The Frontier Justice's guitar smash scene, the smash 3.695 s in (:18103-18107).</summary>
    public const string EngineerGuitarSmashScene = "scenes/player/engineer/low/taunt07.vcd";

    /// <summary>The Gunslinger's arm scene, the impale 3.2 s in (:18108-18112).</summary>
    public const string EngineerArmScene = "scenes/player/engineer/low/taunt09.vcd";

    /// <summary>The Kritzkrieg's inhale scene (Oktoberfest, the talker's MedicTauntKritzkrieg), the heals starting 0.8 s in (:18045-18055).</summary>
    public const string MedicInhaleScene = "scenes/player/medic/low/taunt06.vcd";

    /// <summary>The Ubersaw's slice scene, the impale 2.2 s in (:18056-18060).</summary>
    public const string MedicUbersliceScene = "scenes/player/medic/low/taunt08.vcd";

    /// <summary>The inhale breath's particle names, by team (:18051).</summary>
    public const string InhaleParticleRed = "healhuff_red";
    public const string InhaleParticleBlu = "healhuff_blu";

    /// <summary>The spy fencing scenes' shared head — taunt03_v1 and taunt03_v2 (:18064; 29 characters, case-insensitive).</summary>
    public const string SpyFencingScenePrefix = "scenes/player/spy/low/taunt03";

    /// <summary>The fencing slash B, 0.47 s after the slash A (:18439).</summary>
    public const float FencingSlashDelay = 0.47f;

    /// <summary>The killing stab, 1.73 s after the slash B (:18444).</summary>
    public const float FencingStabDelay = 1.73f;

    /// <summary>The box arm's reach along the flat forward (:18394).</summary>
    public const float BoxReach = 64f;

    /// <summary>The box's half extent, on each axis (:18395).</summary>
    public const float BoxHalfExtent = 24f;

    /// <summary>The radial buff's reach around the heavy, squared-held as stock writes it (:18596).</summary>
    public const float RadialBuffRadius = 1024f;

    /// <summary>The armageddon's reach around the pyro, the distance retest's own (:18764).</summary>
    public const float ArmageddonRadius = 100f;

    /// <summary>The armageddon's pick ceiling, stock's <c>nRandomPick</c> width (:18762).</summary>
    public const int ArmageddonPickCount = 11;

    /// <summary>The hit's position, the attacker's centre carried three quarters of the way to the victim's (:18412-18413).</summary>
    public const float HitLerp = 0.75f;

    /// <summary>The high noon's trace, eye along the full view (:18618).</summary>
    public const float HighNoonRange = 500f;

    /// <summary>The box query's ceiling, stock's <c>pList[256]</c> (:18396).</summary>
    public const int MaxVictims = 256;

    /// <summary>The launch's pitch, up and over (:18401, :18631): the force's direction, never the aim's.</summary>
    public const float LaunchPitch = -45f;

    /// <summary>
    /// The heavy's eat clock, the arm (:17937) and every next bite (:18566)
    /// alike: a second of sandvich each.
    /// </summary>
    public const float HeavyEatSeconds = 1f;

    /// <summary>The scout drink's delay, the arm's own (:17928): nine tenths of a second of raise.</summary>
    public const float ScoutDrinkSeconds = 0.9f;

    /// <summary>The gap the Huntsman's kill takes after its impale (:18536).</summary>
    public const float SniperKillDelaySeconds = 1.3f;

    /// <summary>The inhale's first heal, 0.8 s after the taunt starts (:18047).</summary>
    public const float InhaleStartSeconds = 0.8f;

    /// <summary>The inhale window: heals keep coming while the taunt start's plus 1.8 s is still ahead (:18048).</summary>
    public const float InhaleWindowSeconds = 1.8f;

    /// <summary>The gap between inhale heals (:18886).</summary>
    public const float InhaleRepeatSeconds = 0.1f;

    /// <summary>The Uberslice's impale delay, 2.2 s into its scene (:18058).</summary>
    public const float UbersliceDelaySeconds = 2.2f;

    /// <summary>The gap the Uberslice's kill takes after its impale (:18938).</summary>
    public const float UbersliceKillDelaySeconds = 0.75f;

    /// <summary>The attack waiting to land.</summary>
    /// <summary>The held item's attack-name word (<c>taunt attack name</c>, stock's
    /// <c>GetTauntAttackByName</c>, tf_player.cpp:17483-17488).</summary>
    public const string TauntAttackNameAttribute = "taunt attack name";

    /// <summary>The held item's attack-delay word (<c>taunt attack time</c>, :17490-17494).</summary>
    public const string TauntAttackTimeAttribute = "taunt attack time";

    private enum Attack
    {
        None = 0,
        PyroHadouken,
        HeavyHighNoon,
        HeavyEat,
        ScoutDrink,
        SpyFencingSlashA,
        SpyFencingSlashB,
        SpyFencingStab,
        PyroGasBlast,
        BarbarianSwing,
        ScoutGrandSlam,
        SniperArrowImpale,
        SniperArrowKill,
        SoldierGrenade,
        EngineerGuitarSmash,
        EngineerArmImpale,
        EngineerArmBlend,
        EngineerArmKill,
        MedicInhale,
        MedicUbersliceImpale,
        MedicUbersliceKill,
        PyroArmageddon,
        PyroScorchShot,
        HeavyRadialBuff,
    }

    private Attack _attack = Attack.None;
    private float _fireAt;
    private int _armCount;
    private float _inhaleUntil;
    private EntityHandle _player = EntityHandle.None;
    private readonly ITFLunchboxEffects _lunchbox = lunchbox;
    private readonly ITFWeaponOwners _owners = owners;
    private readonly AttributeHooks _hooks = hooks;
    private readonly IUniformRandom _random = random;
    private readonly IEngineGameEvents _events = events;
    private readonly ITempEnts _temps = temps;
    private readonly ITFMedigunWeapons _mediguns = mediguns;
    private readonly ITFPlayerStats _stats = stats;
    private readonly ITFProjectileEffects _projectile = projectile;

    /// <summary>The scene the standing taunt spoke, whose head the phlog's
    /// rage arm gates on; empty with no taunt (<c>szResponse</c>'s life, the
    /// next speak's overwrite).</summary>
    public string Scene { get; private set; } = "";

    public void Attach(EntityHandle player)
    {
        _player = player;
        Clear();
        carriers.EnterPart(player.EntryIndex, this);
    }

    public void Detach()
    {
        Clear();
        _player = EntityHandle.None;
    }

    /// <summary>
    /// The keying, where stock's <c>Taunt</c> lands it after the speak
    /// (:17874-17875 clearing, then :17979): any pending attack dies with the
    /// new taunt, and the class's scene arms the next. The heavy's fist
    /// alternates (:18025-18033) are keyed on the weapon, not the scene, and
    /// wait with the rest of the non-kill arms.
    /// </summary>
    public void Arm(string scene, float now)
    {
        Clear();

        // Stock's taunt init keys the attack on the held item's own words
        // first (tf_player.cpp:17483-17495): a named attack walks under its
        // own delay, and the time attribute overrides that delay; the class
        // scene's keying answers only behind them. Stock matches the name
        // against the enum spellings, case-insensitively
        // (tf_shareddefs.cpp:1066-1075).
        IAttributeProvider? held = _owners.ActiveItemOf(_player.EntryIndex);
        string attackName = _hooks.String("", TauntAttackNameAttribute, held) ?? "";
        Attack named = attackName.ToUpperInvariant() switch
        {
            "TAUNTATK_PYRO_HADOUKEN" => Attack.PyroHadouken,
            "TAUNTATK_PYRO_GASBLAST" => Attack.PyroGasBlast,
            "TAUNTATK_HEAVY_HIGHNOON" => Attack.HeavyHighNoon,
            "TAUNTATK_SPY_FENCING_SLASH_A" => Attack.SpyFencingSlashA,
            _ => Attack.None,
        };
        if (named != Attack.None)
        {
            float delay = _hooks.Float(0f, TauntAttackTimeAttribute, held);
            Arm(named, delay > 0f ? delay : named switch
            {
                Attack.HeavyHighNoon => 1.8f,
                Attack.SpyFencingSlashA => 1.8f,
                Attack.SpyFencingSlashB => FencingSlashDelay,
                Attack.SpyFencingStab => FencingStabDelay,
                _ => 2.1f,   // the pyro arms' scene lengths (:17982-17986)
            }, now);
            Scene = scene;
            return;
        }

        switch (players.ClassOf(_player))
        {
            case TFClass.Pyro when SceneIs(scene, PyroHadoukenScene):
                Arm(Attack.PyroHadouken, 2.1f, now);
                break;

            // The Rainblower's armageddon (:17987-17990): its own scene, the round
            // of plasma 3 s in. The rainbow's info-target parent and its particle
            // (:17992-18010) are the visual halves this build leaves out, named here.
            case TFClass.Pyro when SceneIs(scene, PyroArmageddonScene):
                Arm(Attack.PyroArmageddon, 3.0f, now);
                break;

            // The Scorch Shot (:18012-18015): its own scene, the held flare gun's
            // launch 1.9 s in.
            case TFClass.Pyro when SceneIs(scene, PyroScorchShotScene):
                Arm(Attack.PyroScorchShot, 1.9f, now);
                break;

            // The scout's grand slam (:18036-18042): the scene's keying, the
            // stronger one — in stock the scene's write lands after the drink
            // arm's (:17922-17932) and overwrites it, so it stands first here.
            case TFClass.Scout when SceneIs(scene, ScoutGrandSlamScene):
                Arm(Attack.ScoutGrandSlam, 4.03f, now);
                break;

            // The scout's drink arm (:17922-17932): the lunchbox family's
            // drink in the hand and no phase on — the buff's own remainder
            // refuses the next press, as the drink's arm does in stock.
            case TFClass.Scout when WeaponOf() == TFWeaponId.Lunchbox && !players.InCond(_player, TFCond.Phase):
                Arm(Attack.ScoutDrink, ScoutDrinkSeconds, now);
                break;

            case TFClass.HeavyWeapons when SceneIs(scene, HeavyHighNoonScene):
                Arm(Attack.HeavyHighNoon, 1.8f, now);
                break;

            // The fists' radial buff (:18025-18033): the fist alternates are keyed
            // on the held fists and their hook-read fist type, not on the scene —
            // the radial's own mode, FISTTYPE_RADIAL_BUFF (tf_weapon_fists.h:21-22),
            // is <c>set_weapon_mode</c> 1 as <c>GetFistType</c> answers it (:60).
            case TFClass.HeavyWeapons when WeaponOf() == TFWeaponId.Fists
                                           && _hooks.Int(0, "set_weapon_mode", held) == 1:
                Arm(Attack.HeavyRadialBuff, 1.0f, now);
                break;

            // The sandvich arm is the weapon's, not the scene's (:17933-17947):
            // any taunt scene while the lunchbox is the hand arms the eat. The
            // eat response is never the high noon's, so the scene's arm stands
            // first here as stock's later keying does.
            case TFClass.HeavyWeapons when WeaponOf() == TFWeaponId.Lunchbox:
                Arm(Attack.HeavyEat, HeavyEatSeconds, now);
                break;

            // The medic's inhale (:18045-18055): heals from 0.8 s in while the
            // 1.8 s inhale time stands, the breath particle keyed with it.
            case TFClass.Medic when SceneIs(scene, MedicInhaleScene):
                _inhaleUntil = now + InhaleWindowSeconds;
                BreathParticle();
                Arm(Attack.MedicInhale, InhaleStartSeconds, now);
                break;

            // The Uberslice (:18056-18060): the impale 2.2 s into its scene,
            // the kill re-armed behind it (:18935-18939).
            case TFClass.Medic when SceneIs(scene, MedicUbersliceScene):
                Arm(Attack.MedicUbersliceImpale, UbersliceDelaySeconds, now);
                break;

            case TFClass.Spy when scene.StartsWith(SpyFencingScenePrefix, StringComparison.OrdinalIgnoreCase):
                Arm(Attack.SpyFencingSlashA, 1.8f, now);
                break;

            // The Huntsman's arrow stab (:18070-18076): the impale lands
            // 0.85 s into its scene, and re-arms the kill behind it.
            case TFClass.Sniper when SceneIs(scene, SniperArrowScene):
                Arm(Attack.SniperArrowImpale, 0.85f, now);
                break;

            // The Equalizer's grenade kill (:18078-18091): the blast 3.5 s
            // into its scene. Stock's Worms-gear variant (:18082-18087) is
            // left out here too — this build equips no such cosmetic.
            case TFClass.Soldier when SceneIs(scene, SoldierGrenadeScene):
                Arm(Attack.SoldierGrenade, 3.5f, now);
                break;

            // The Eyelander's barbarian swing (:18093-18100): its own scene,
            // the swing landing 2.55 s in.
            case TFClass.Demoman when SceneIs(scene, DemomanBarbarianScene):
                Arm(Attack.BarbarianSwing, 2.55f, now);
                break;

            // The Frontier Justice's guitar smash (:18103-18107): its own scene,
            // the smash landing 3.695 s in.
            case TFClass.Engineer when SceneIs(scene, EngineerGuitarSmashScene):
                Arm(Attack.EngineerGuitarSmash, 3.695f, now);
                break;

            // The Gunslinger's arm (:18108-18112): the impale 3.2 s into its
            // scene, then the blend chain behind it (:18538-18553).
            case TFClass.Engineer when SceneIs(scene, EngineerArmScene):
                Arm(Attack.EngineerArmImpale, 3.2f, now);
                break;
        }

        Scene = scene;
    }

    /// <summary>The pending attack, dropped: stock's zeroing before the keying (:17874-17875), the scene with it.</summary>
    public void Clear() => (_attack, _fireAt, Scene) = (Attack.None, 0f, "");

    /// <summary>
    /// PostThink's window (:2691-2695): a set time past <paramref name="now"/>
    /// fires once, the time cleared first so the arm's own re-arm is the one
    /// that stands.
    /// </summary>
    public void Tick(float now)
    {
        if (_attack == Attack.None || _fireAt == 0f || _fireAt >= now)
            return;

        _fireAt = 0f;
        Fire(_attack, now);
    }

    /// <summary><c>DoTauntAttack</c>'s gate (:18379-18382): no taunt or no life, nothing — and the chain with it.</summary>
    private void Fire(Attack attack, float now)
    {
        _attack = Attack.None;

        if (!players.InCond(_player, TFCond.Taunting) || !players.IsAlive(_player))
            return;

        if (attack == Attack.HeavyHighNoon)
            HighNoon();
        else if (attack == Attack.HeavyEat)
        {
            // The bite, the held lunchbox's only (:18557-18562); then the next
            // one, a second on, till the condition ends (:18564-18566 — the
            // gate at the head is the chain's end either way).
            if (WeaponOf() == TFWeaponId.Lunchbox)
                _lunchbox.Bite(_player.EntryIndex);

            Arm(Attack.HeavyEat, HeavyEatSeconds, now);
        }
        else if (attack == Attack.ScoutDrink)
        {
            // The drink (:18636-18675): the lunchbox family's in the hand, the
            // stun gate and the cond the mode names are the drink's own
            // (:18638-18673). Once per press — the phase the plain drink leaves
            // behind refuses the next arm (:17924).
            if (WeaponOf() == TFWeaponId.Lunchbox)
                _lunchbox.Drink(_player.EntryIndex);
        }
        else if (attack == Attack.BarbarianSwing)
            BarbarianSwing();
        else if (attack == Attack.ScoutGrandSlam)
            GrandSlam();
        else if (attack == Attack.SniperArrowImpale)

            ArrowImpale();
        else if (attack == Attack.SniperArrowKill)
            ArrowKill();
        else if (attack == Attack.SoldierGrenade)
            GrenadeKill();
        else if (attack == Attack.EngineerGuitarSmash)
            GuitarSmash();
        else if (attack == Attack.EngineerArmImpale)
            ArmImpale();
        else if (attack == Attack.EngineerArmBlend)
            ArmBlend();
        else if (attack == Attack.EngineerArmKill)
            ArmKill();
        else if (attack == Attack.MedicInhale)
            Inhale();
        else if (attack == Attack.MedicUbersliceImpale)
            UbersliceImpale();
        else if (attack == Attack.MedicUbersliceKill)
            UbersliceKill();
        else if (attack == Attack.PyroScorchShot)
            ScorchShot(now);
        else if (attack == Attack.HeavyRadialBuff)
            RadialBuff(now);
        else if (attack == Attack.PyroArmageddon)
            Armageddon();
        else
            BoxAttack(attack);

        // The chain, whether or not the box held anyone (:18436-18445).
        switch (attack)
        {
            case Attack.SpyFencingSlashA:
                Arm(Attack.SpyFencingSlashB, FencingSlashDelay, now);
                break;
            case Attack.SpyFencingSlashB:
                Arm(Attack.SpyFencingStab, FencingStabDelay, now);
                break;
            case Attack.SniperArrowImpale:
                // :18533-18536: the kill follows the impale 1.30 s on, hit or not.
                Arm(Attack.SniperArrowKill, SniperKillDelaySeconds, now);
                break;
            case Attack.EngineerArmImpale:
                // :18538-18543: the blend follows the impale 0.05 s on, the count zeroed.
                _armCount = 0;
                Arm(Attack.EngineerArmBlend, 0.05f, now);
                break;
            case Attack.EngineerArmBlend:
                // :18544-18553: each blend re-arms itself 0.05 s on and counts up;
                // the thirteenth hands over to the kill.
                _armCount++;
                Arm(_armCount == 13 ? Attack.EngineerArmKill : Attack.EngineerArmBlend, 0.05f, now);
                break;
            case Attack.MedicUbersliceImpale:
                // :18935-18939: the kill follows the impale 0.75 s on, hit or not.
                Arm(Attack.MedicUbersliceKill, UbersliceKillDelaySeconds, now);
                break;
            case Attack.MedicInhale:
                // :18883-18887: another heal 0.1 s on while the inhale time of
                // the press is still ahead; past it the chain ends.
                if (_inhaleUntil > now)
                    Arm(Attack.MedicInhale, InhaleRepeatSeconds, now);
                break;
        }
    }

    private void Arm(Attack attack, float delay, float now) => (_attack, _fireAt) = (attack, now + delay);

    /// <summary>
    /// The box arm (:18387-18451): everything in the volume an arm's reach
    /// ahead, seen from the centre, but the taunter themselves and whoever a
    /// wall hides, taking the hit at the point carried three quarters of the
    /// way over, thrown up at 45°.
    /// </summary>
    private void BoxAttack(Attack attack)
    {
        float yaw = entities.GetEyeAngles(_player).Y;
        Vector flatForward = Angles.Forward(new Vector(0f, yaw, 0f)); // :18393, the pitch flattened away
        Vector center = entities.GetWorldSpaceCenter(_player) + flatForward * BoxReach;
        Vector halfExtent = new(BoxHalfExtent, BoxHalfExtent, BoxHalfExtent);

        (float hurt, DamageType type, TFDamageCustom custom, float push) = attack switch
        {
            Attack.PyroHadouken => (500f, DamageType.Burn | TFDamageBits.Ignite, TFDamageCustom.TauntatkHadouken, 25000f),
            Attack.PyroGasBlast => (500f, DamageType.Burn | TFDamageBits.Ignite, TFDamageCustom.TauntatkGasblast, 25000f),   // :18429-18431: the gasblast's box, same numbers, its own custom
            Attack.SpyFencingSlashA or Attack.SpyFencingSlashB
                => (25f, DamageType.Slash | DamageType.PreventPhysicsForce, TFDamageCustom.TauntatkFencing, 100f),
            Attack.SpyFencingStab => (500f, DamageType.Slash, TFDamageCustom.TauntatkFencing, 20000f),
            _ => (0f, DamageType.None, TFDamageCustom.None, 0f),
        };
        if (hurt <= 0f)
            return;

        // The launch, one direction for the whole swing (:18401): no push
        // rides the slashes, so the victim stays in the stab's box.
        Vector launch = Angles.Forward(new Vector(LaunchPitch, yaw, 0f)) * push;

        Span<EntityHandle> nearby = stackalloc EntityHandle[MaxVictims];
        int count = world.EntitiesInSphere(center, BoxCornerRadius, nearby);

        for (int i = 0; i < count; i++)
        {
            EntityHandle victim = nearby[i];
            if (victim == EntityHandle.None || victim == _player)
                continue;

            // FL_CLIENT | FL_OBJECT (const.h:118, :141) as this build says it:
            // a player, or a solid entity of ours the directory carries. The
            // player's own data carrier is an effect, and no list member.
            if (!networking.EntityGetState(victim, out _).HasFlag(EntityState.Solid)
                || !(entities.IsPlayer(victim) || directory.Get(victim) is ScriptedEntity))
                continue;

            // The box, exact: the sphere is the sweep's candidate cut, the
            // collision bounds the retest stock's partition makes.
            if (!OverlapsBox(victim, center - halfExtent, center + halfExtent))
                continue;

            // FVisible (MASK_SOLID, :18409): nothing but the victim stops the
            // line from the eye to its centre (baseentity.cpp's FVisible).
            Vector victimCenter = entities.GetWorldSpaceCenter(victim);
            TraceResult seen = world.TraceLine(entities.GetEyePosition(_player), victimCenter, TraceMask.Solid, _player);
            if (seen.Hit && seen.Entity != victim)
                continue;

            Vector myCenter = entities.GetWorldSpaceCenter(_player);
            Vector hit = myCenter + (victimCenter - myCenter) * HitLerp;

            using (scopes.Push(custom, WeaponOf(), WeaponNameOf()))
                damage.ApplyAt(victim, _player, hurt, type, launch, hit);
        }
    }

    /// <summary>
    /// The heavy's high noon (:18613-18635): the trace from the eye along the
    /// full view — hitboxes included, <see cref="TraceMask.SolidHitbox"/>'s
    /// reason — and what it stops on, a player of another real team, takes
    /// the shot, launched as the box arm's are.
    /// </summary>
    private void HighNoon()
    {
        Vector eyeAngles = entities.GetEyeAngles(_player);
        Vector eye = entities.GetEyePosition(_player);
        TraceResult tr = world.TraceLine(eye, eye + Angles.Forward(eyeAngles) * HighNoonRange, TraceMask.SolidHitbox, _player);

        if (tr.Fraction >= 1f || tr.Entity == EntityHandle.None || !entities.IsPlayer(tr.Entity))
            return;

        // :18628 — past the shared teams, and not ours.
        int team = enginePlayers.GetTeam(tr.Entity);
        if (team <= (int)TFTeam.Spectator || team == enginePlayers.GetTeam(_player))
            return;

        Vector launch = Angles.Forward(new Vector(LaunchPitch, eyeAngles.Y, 0f)) * 25000f;
        using (scopes.Push(TFDamageCustom.TauntatkHighNoon, WeaponOf(), WeaponNameOf()))
            damage.ApplyAt(tr.Entity, _player, 500f, DamageType.Bullet, launch, entities.GetWorldSpaceCenter(_player));
    }

    /// <summary>
    /// The Eyelander's barbarian swing (:18941-18961): a trace of 128 units from
    /// the eye along the full view — <see cref="TraceMask.Solid"/>'s contents
    /// never carry the HITBOX bit, so stock's <c>MASK_SOLID &amp; ~CONTENTS_HITBOX</c>
    /// is just <c>MASK_SOLID</c> here — and what it stops on, a player of another
    /// real team, takes the swing: 500 DMG_CLUB, thrown from the victim towards
    /// the taunter at 12000, stock's sign kept as written (:18956-18958).
    /// </summary>
    private void BarbarianSwing()
    {
        Vector eyeAngles = entities.GetEyeAngles(_player);
        Vector eye = entities.GetEyePosition(_player);
        TraceResult tr = world.TraceLine(eye, eye + Angles.Forward(eyeAngles) * 128f, TraceMask.Solid, _player);

        if (tr.Fraction >= 1f || tr.Entity == EntityHandle.None || !entities.IsPlayer(tr.Entity))
            return;

        // :18954 — past the shared teams, and not ours.
        int team = enginePlayers.GetTeam(tr.Entity);
        if (team <= (int)TFTeam.Spectator || team == enginePlayers.GetTeam(_player))
            return;

        // The throw runs from the victim towards the taunter (:18956-18957).
        Vector throwDirection = (entities.GetWorldSpaceCenter(_player) - entities.GetWorldSpaceCenter(tr.Entity)).Normalized;
        Vector launch = throwDirection * 12000f;

        using (scopes.Push(TFDamageCustom.TauntatkBarbarianSwing, WeaponOf(), WeaponNameOf()))
            damage.ApplyAt(tr.Entity, _player, 500f, DamageType.Club, launch, entities.GetWorldSpaceCenter(_player));
    }

    /// <summary>
    /// The Sandman's grand slam (:18676-18730): <see cref="BoxAttack"/>'s box —
    /// its reach, half extent, candidate cut and contents — with the scout's own
    /// gates: the facing dot of at least 0.80, players only, other teams only,
    /// and the brush-only sight line that must reach the victim whole
    /// (:18690-18710). What lands on each: the velocity zeroed, the view pitched
    /// up by a random 10 to 15 (:18712-18715), then 500 DMG_BULLET on the -45°
    /// launch of 130000 (:18717-18718) and the achievement event with both user
    /// ids (:18720-18727). Stock's stun sound (:18714) is the half the stun lane
    /// leaves out (<see cref="TFStunStack"/>'s note); the swing stands without it
    /// here too.
    /// </summary>
    private void GrandSlam()
    {
        float yaw = entities.GetEyeAngles(_player).Y;
        Vector flatForward = Angles.Forward(new Vector(0f, yaw, 0f)); // :18680-18681, the pitch flattened away
        Vector myCenter = entities.GetWorldSpaceCenter(_player);
        Vector center = myCenter + flatForward * BoxReach;
        Vector halfExtent = new(BoxHalfExtent, BoxHalfExtent, BoxHalfExtent);

        Span<EntityHandle> nearby = stackalloc EntityHandle[MaxVictims];
        int count = world.EntitiesInSphere(center, BoxCornerRadius, nearby);

        for (int i = 0; i < count; i++)
        {
            EntityHandle victim = nearby[i];
            if (victim == EntityHandle.None || victim == _player)
                continue;

            // FL_CLIENT | FL_OBJECT (:18685): the same box contents as the other arms'.
            if (!networking.EntityGetState(victim, out _).HasFlag(EntityState.Solid)
                || !(entities.IsPlayer(victim) || directory.Get(victim) is ScriptedEntity))
                continue;

            if (!OverlapsBox(victim, center - halfExtent, center + halfExtent))
                continue;

            // Must be facing whoever we knock back (:18690-18696).
            Vector victimCenter = entities.GetWorldSpaceCenter(victim);
            if (flatForward.Dot((victimCenter - myCenter).Normalized) < 0.80f)
                continue;

            // Players only, other teams only (:18698-18703).
            if (!entities.IsPlayer(victim) || enginePlayers.GetTeam(victim) == enginePlayers.GetTeam(_player))
                continue;

            // The sight line (:18705-18710): centre to centre, MASK_SOLID_BRUSHONLY —
            // what <see cref="TraceMask.Brushes"/> answers — and fraction 1 is clear.
            if (world.TraceLine(myCenter, victimCenter, TraceMask.Brushes, _player).Fraction < 1f)
                continue;

            // Stand still, look up, fly (:18712-18718). Stock's stun sound is the
            // half this build leaves out; the punch velocity rides the carrier as
            // the damage layer's punch does.
            entities.SetVelocity(victim, default);
            Vector punch = enginePlayers.GetPunch(victim);
            Vector punchVelocity = directory.Get(victim) is SsPlayer holder ? holder.State.PunchAngleVel.AsVector() : default;
            enginePlayers.SetPunch(victim, new Vector(punch.X + _random.RandomInt(10, 15), punch.Y, punch.Z), punchVelocity);

            Vector launch = Angles.Forward(new Vector(LaunchPitch, yaw, 0f)) * 130000f;
            using (scopes.Push(TFDamageCustom.TauntatkGrandSlam, WeaponOf(), WeaponNameOf()))
                damage.ApplyAt(victim, _player, 500f, DamageType.Bullet, launch, myCenter);

            // The achievement event (:18720-18727).
            IntPtr e = _events.EventCreate("scout_grand_slam");
            if (e != IntPtr.Zero)
            {
                _events.EventSetInt(e, "scout_id", enginePlayers.GetUserId(_player));
                _events.EventSetInt(e, "target_id", enginePlayers.GetUserId(victim));
                _events.EventFire(e);
            }
        }
    }

    /// <summary>
    /// The arrow's shared line (:18481-18492): 128 units from the eye along the
    /// full view, <see cref="TraceMask.Solid"/>'s contents never carry the HITBOX
    /// bit so stock's <c>MASK_SOLID &amp; ~CONTENTS_HITBOX</c> is just
    /// <c>MASK_SOLID</c> (:18486), and what it stops on, a player of another real
    /// team, is the victim. The same gates as the barbarian swing's.
    /// </summary>
    private EntityHandle ArrowTraceVictim()
    {
        Vector eyeAngles = entities.GetEyeAngles(_player);
        Vector eye = entities.GetEyePosition(_player);
        TraceResult tr = world.TraceLine(eye, eye + Angles.Forward(eyeAngles) * 128f, TraceMask.Solid, _player);

        if (tr.Fraction >= 1f || tr.Entity == EntityHandle.None || !entities.IsPlayer(tr.Entity))
            return EntityHandle.None;

        // :18492 — past the shared teams, and not ours.
        int team = enginePlayers.GetTeam(tr.Entity);
        if (team <= (int)TFTeam.Spectator || team == enginePlayers.GetTeam(_player))
            return EntityHandle.None;

        return tr.Entity;
    }

    /// <summary>
    /// The impale (:18498-18506): the traced victim is held — 3 s, full
    /// strength, movement and controls with no stun particles, no damage.
    /// Stock's giant refusal (:18502-18504) has no MvM state to read in this
    /// build (<see cref="TFPlayerGib"/>'s note), so the stun lands whole.
    /// </summary>
    private void ArrowImpale()
    {
        EntityHandle victim = ArrowTraceVictim();
        if (victim == EntityHandle.None)
            return;

        carriers.Part<TFStunStack>(victim.EntryIndex)
            ?.Stun(victim, 3f, 1f, TFStunFlags.Movement | TFStunFlags.Controls | TFStunFlags.NoEffects, _player);
    }

    /// <summary>
    /// The kill (:18519-18524): the line again — the victim may have stepped out
    /// since the impale — and on it 500 DMG_BULLET|DMG_PREVENT_PHYSICS_FORCE at
    /// the victim's centre, thrown from the victim towards the taunter at 12000
    /// (:18521-18523, stock's sign kept as written).
    /// </summary>
    private void ArrowKill()
    {
        EntityHandle victim = ArrowTraceVictim();
        if (victim == EntityHandle.None)
            return;

        Vector throwDirection = (entities.GetWorldSpaceCenter(_player) - entities.GetWorldSpaceCenter(victim)).Normalized;
        Vector launch = throwDirection * 12000f;

        using (scopes.Push(TFDamageCustom.TauntatkArrowStab, WeaponOf(), WeaponNameOf()))
            damage.ApplyAt(victim, _player, 500f, DamageType.Bullet | DamageType.PreventPhysicsForce, launch,
                           entities.GetWorldSpaceCenter(victim));
    }

    /// <summary>
    /// The Equalizer's grenade kill (:18458-18476): at the right hand's bone a
    /// TF explosion and a blast of 200 over a radius of 100 — stock reads
    /// <c>bip_hand_r</c> through <c>LookupBone</c> and its position through
    /// <c>GetBonePosition</c> (<see cref="IEngineEntities.LookupBone"/>,
    /// <see cref="IEngineEntities.GetBonePosition"/>, the managed skeleton's
    /// bone build answering): no named bone, no blast, as stock's own gate
    /// does (:18466). The blast goes through our TF radius damage as the
    /// rockets call it, and it hurts the taunter too — no ignore rides the sweep
    /// (the stickbomb's Kaboom is the same shape). The explosion's sprite index
    /// is the engine's default fireball, the satchel's own read.
    /// </summary>
    private void GrenadeKill()
    {
        int hand = entities.LookupBone(_player, "bip_hand_r");   // stock's name (:18465)
        if (hand < 0)                                             // stock's missing-bone gate (:18466)
            return;

        if (!entities.GetBonePosition(_player, hand, out Vector handPos))
            return;

        // The explosion effect (:18470-18471), our engines' own shape (:244-245
        // of the satchel's blast): scale rides the radius, the rate is the stock
        // frame rate, the flags none.
        int sprite = precache.PrecacheModel("sprites/zerogxplode.vmt");
        effects.ExplosionEffect(handPos, sprite, 100f * 0.03f, 25, 0, 100, 200);

        blast.Apply(handPos, TFBlast.Of(200f, 100f, DamageType.Blast | TFDamageBits.UseDistanceMod),
                    _player, _player, ignored: EntityHandle.None, WeaponOf(), WeaponNameOf(), critical: false,
                    custom: TFDamageCustom.TauntatkGrenade);
    }

    /// <summary>
    /// The Frontier Justice's guitar smash (:18962-18980): <see cref="ArrowTraceVictim"/>'s
    /// line and gates — the same 128-unit eye trace, the same mask, a player of another
    /// real team (:18969-18975) — and on it 500 DMG_CLUB at the taunter's centre, thrown
    /// towards the taunter at a gentle twelve (:18977-18979, stock's sign kept as written).
    /// </summary>
    private void GuitarSmash()
    {
        EntityHandle victim = ArrowTraceVictim();
        if (victim == EntityHandle.None)
            return;

        // The throw runs from the victim towards the taunter (:18977-18978).
        Vector throwDirection = (entities.GetWorldSpaceCenter(_player) - entities.GetWorldSpaceCenter(victim)).Normalized;
        Vector launch = throwDirection * 12f;

        using (scopes.Push(TFDamageCustom.TauntatkEngineerGuitarSmash, WeaponOf(), WeaponNameOf()))
            damage.ApplyAt(victim, _player, 500f, DamageType.Club, launch, entities.GetWorldSpaceCenter(_player));
    }

    /// <summary>
    /// The Gunslinger's impale (:18498-18513): the shared line's victim is held 3 s at
    /// full strength with no stun particles (:18505, the giant refusal as the Huntsman's —
    /// see <see cref="ArrowImpale"/>'s note), then punched for 1 DMG_BULLET|PREVENT_PHYSICS_FORCE
    /// along the eye forward, at the victim's centre (:18508-18511).
    /// </summary>
    private void ArmImpale()
    {
        EntityHandle victim = ArrowTraceVictim();
        if (victim == EntityHandle.None)
            return;

        carriers.Part<TFStunStack>(victim.EntryIndex)
            ?.Stun(victim, 3f, 1f, TFStunFlags.Movement | TFStunFlags.Controls | TFStunFlags.NoEffects, _player);

        Vector eyeForward = Angles.Forward(entities.GetEyeAngles(_player));
        using (scopes.Push(TFDamageCustom.TauntatkEngineerArmKill, WeaponOf(), WeaponNameOf()))
            damage.ApplyAt(victim, _player, 1f, DamageType.Bullet | DamageType.PreventPhysicsForce, eyeForward,
                           entities.GetWorldSpaceCenter(victim));
    }

    /// <summary>
    /// The Gunslinger's blend (:18515-18517): the line again, and another punch of 1
    /// DMG_BULLET|PREVENT_PHYSICS_FORCE along the eye forward at the victim's centre,
    /// the arm custom riding it. The chain itself (:18544-18553) is the re-arms' work.
    /// </summary>
    private void ArmBlend()
    {
        EntityHandle victim = ArrowTraceVictim();
        if (victim == EntityHandle.None)
            return;

        Vector eyeForward = Angles.Forward(entities.GetEyeAngles(_player));
        using (scopes.Push(TFDamageCustom.TauntatkEngineerArmKill, WeaponOf(), WeaponNameOf()))
            damage.ApplyAt(victim, _player, 1f, DamageType.Bullet | DamageType.PreventPhysicsForce, eyeForward,
                           entities.GetWorldSpaceCenter(victim));
    }

    /// <summary>
    /// The Gunslinger's kill (:18526-18528): the line once more, and on it 500 DMG_BLAST
    /// at the victim's centre, thrown on the eye forward of 12000 — the engineer's kill
    /// keeps the aim's own direction, unlike the sniper's re-normalised throw (:18521-18523).
    /// </summary>
    private void ArmKill()
    {
        EntityHandle victim = ArrowTraceVictim();
        if (victim == EntityHandle.None)
            return;

        Vector launch = Angles.Forward(entities.GetEyeAngles(_player)) * 12000f;
        using (scopes.Push(TFDamageCustom.TauntatkEngineerArmKill, WeaponOf(), WeaponNameOf()))
            damage.ApplyAt(victim, _player, 500f, DamageType.Blast, launch, entities.GetWorldSpaceCenter(victim));
    }

    /// <summary>The particle record's own flags: the record rides the entity, and the
    /// follow attach types (<c>particle_parse.cpp:329-330</c>, the <c>PATTACH_POINT_FOLLOW</c>
    /// of :18052).</summary>
    private const int ParticleFromEntity = 1 << 0;
    private const int ParticleAttachPointFollow = 4;

    /// <summary>
    /// The inhale's breath (:18050-18052): the <c>healhuff_red</c>/<c>healhuff_blu</c>
    /// particle keyed on the taunter's team, dispatched at the <c>eyes</c> attachment as
    /// a following point — the record names the entity, the attach type and the looked-up
    /// attachment, the shape <c>TriggerParticle</c> carries. The broadcast filter is the
    /// follow types' own (:18052).
    /// </summary>
    private void BreathParticle()
    {
        string breath = enginePlayers.GetTeam(_player) == (int)TFTeam.Red ? InhaleParticleRed : InhaleParticleBlu;
        precache.PrecacheParticleSystem(breath);   // stock's DispatchParticleEffect precaches the named system
        TeEffectData data = new()
        {
            Scale = 1f,
            HitBox = precache.ParticleSystemIndex(breath),
            EntIndex = _player.EntryIndex,
            Flags = ParticleFromEntity,
            Origin = entities.GetOrigin(_player),
            DamageType = ParticleAttachPointFollow,
            AttachmentIndex = entities.LookupAttachmentAny(_player, "eyes"),
        };
        _temps.DispatchEffect(TeFilters.Broadcast, 0f, "ParticleEffect", in data);
    }

    /// <summary>
    /// The Kritzkrieg's inhale (:18873-18887): a point of health, capped at the medic's own
    /// maximum as every heal is; what was taken books the medic's healing stat (:18877-18880).
    /// The chain's re-arm is the switch after <see cref="Fire"/>'s.
    /// </summary>
    private void Inhale()
    {
        int healed = players.TakeHealth(_player, 1f);   // :18875 — TakeHealth(1, DMG_GENERIC), held at the class max
        if (healed > 0)
            _stats.Increment(_player, TFStat.Healing, healed);   // :18877-18880 — Event_PlayerHealedOther
    }

    /// <summary>
    /// The Uberslice's impale (:18906-18915): the shared line's victim is held 1.5 s at full
    /// strength with no stun particles (:18913, the giant refusal as the Huntsman's — see
    /// <see cref="ArrowImpale"/>'s note), then punched for 1 DMG_BULLET|PREVENT_PHYSICS_FORCE
    /// along the eye forward, at the taunter's centre (:18915).
    /// </summary>
    private void UbersliceImpale()
    {
        EntityHandle victim = ArrowTraceVictim();
        if (victim == EntityHandle.None)
            return;

        carriers.Part<TFStunStack>(victim.EntryIndex)
            ?.Stun(victim, 1.5f, 1f, TFStunFlags.Movement | TFStunFlags.Controls | TFStunFlags.NoEffects, _player);

        Vector eyeForward = Angles.Forward(entities.GetEyeAngles(_player));
        using (scopes.Push(TFDamageCustom.TauntatkUberslice, WeaponOf(), WeaponNameOf()))
            damage.ApplyAt(victim, _player, 1f, DamageType.Bullet | DamageType.PreventPhysicsForce, eyeForward,
                           entities.GetWorldSpaceCenter(_player));
    }

    /// <summary>
    /// The Uberslice's kill (:18918-18931): the line once more, and on it 500
    /// DMG_BULLET|DMG_PREVENT_PHYSICS_FORCE at the taunter's centre, thrown on the normalized
    /// line from the victim to the taunter of 12000 (:18921-18924, stock's sign kept as written);
    /// the medic's own medigun then gains half a charge (:18926-18930).
    /// </summary>
    private void UbersliceKill()
    {
        EntityHandle victim = ArrowTraceVictim();
        if (victim == EntityHandle.None)
            return;

        Vector launch = (entities.GetWorldSpaceCenter(_player) - entities.GetWorldSpaceCenter(victim)).Normalized * 12000f;
        using (scopes.Push(TFDamageCustom.TauntatkUberslice, WeaponOf(), WeaponNameOf()))
            damage.ApplyAt(victim, _player, 500f, DamageType.Bullet | DamageType.PreventPhysicsForce, launch,
                           entities.GetWorldSpaceCenter(_player));

        // The medic's ÜberCharge meter, half a charge on the kill (:18926-18930);
        // the meter's clamp to one is the setter's, as everywhere else here.
        float charge = _mediguns.ChargeLevel(_player.EntryIndex);
        _mediguns.SetChargeLevel(_player.EntryIndex, charge + 0.5f);
    }

    /// <summary>
    /// The Scorch Shot's launch (:18837-18848): the held gun's id decides — a flare gun,
    /// the manmelter's own revenge id among them not (:18840) — and the flare goes out once
    /// through the launch seam, the script's own damage with no charge behind it (:18845).
    /// </summary>
    private void ScorchShot(float now)
    {
        string name = WeaponNameOf();
        if (scripts.Get(name) is not { } script || script.Id != TFWeaponId.Flaregun)
            return;

        // One shot on the seam (:18845): the held gun's own damage, no charge, the owner's
        // index riding the command so the flare is born to him.
        var attack = new WeaponAttackContext(default!, new WeaponNetState(), new ViewModelNetState(),
                                             now, EffectsLive: true, null!,
                                             new WeaponCommand(0, _player.EntryIndex, _player.EntryIndex));
        _projectile.Projectile(in attack, script, new ShotDamage(script.Primary.Damage), charge: 0f);
    }

    /// <summary>
    /// The fists' radial buff (:18585-18611): every living mate of the taunter's own team within
    /// the squared reach (:18590-18596 — stock walks the team's list, the number is the gate) takes
    /// 50 health held at their own maximum (:18598) and the heal's own <c>player_healonhit</c> event,
    /// amount, healed edict and stock's <c>-1</c> defindex among its fields (:18601-18608). The
    /// half-second crit bonus (:18599) pays a half of the 0..1 amount onto the taunter's own crit
    /// meter as one generic event (<see cref="TFCritMultTicker.AddTempCritBonus"/>, :9683-9696).
    /// </summary>
    private void RadialBuff(float now)
    {
        Vector origin = entities.GetOrigin(_player);

        Span<EntityHandle> nearby = stackalloc EntityHandle[MaxVictims];
        int count = world.EntitiesInSphere(origin, RadialBuffRadius, nearby);

        for (int i = 0; i < count; i++)
        {
            EntityHandle mate = nearby[i];
            if (mate == EntityHandle.None || !entities.IsPlayer(mate) || !players.IsAlive(mate))   // :18593
                continue;

            // Stock walks the team's own players (:18590-18592): the number is the gate.
            if (enginePlayers.GetTeam(mate) != enginePlayers.GetTeam(_player))
                continue;

            // The squared reach, strict as stock writes it (:18596).
            if ((origin - entities.GetOrigin(mate)).LengthSquared >= RadialBuffRadius * RadialBuffRadius)
                continue;

            players.TakeHealth(mate, 50f);   // :18598 — TakeHealth(50, DMG_GENERIC), capped like every heal

            // The heal's event (:18601-18608): the held item's defindex, none here, is stock's own -1 (:18606).
            IntPtr e = _events.EventCreate("player_healonhit");
            if (e != IntPtr.Zero)
            {
                _events.EventSetInt(e, "amount", 50);                            // :18604
                _events.EventSetInt(e, "entindex", entities.GetIndex(mate));    // :18605
                _events.EventSetInt(e, "weapon_def_index", -1);                  // INVALID_ITEM_DEF_INDEX (:18606)
                _events.EventFire(e);
            }
        }

        // The half-second crit bonus (:18599): the fists' half pays one generic event onto the
        // taunter's crit meter, 0.5 remapped onto the 800-point scale and divided across the
        // multiplier range (:9683-9696).
        carriers.Part<TFCritMultTicker>(_player.EntryIndex)?.AddTempCritBonus(0.5f, now);
    }

    /// <summary>
    /// The Rainblower's armageddon (:18755-18836): enemies only — not one of ours, not invulnerable
    /// (:18770-18773), the squared distance retest standing over the box query's candidates
    /// (:18775-18779), the brush-only sight line reaching him whole (:18781-18790). The picked round
    /// order is stock's (:18800-18822): eleven slots walk backwards, each pick drawn between 0 and its
    /// turn and overwritten by the end of the list. The first enemy takes 400 plasma with the armageddon
    /// custom, each next half again (:18798, :18809, :18818). The explosion temp-ent and screen shake
    /// (:18760, :18834), the rainbow's info-target parent (:17992-18010) and the burn-counted achievement
    /// (:18797, :18812-18814, :18825-18831) are the halves this build leaves out, named here.
    /// </summary>
    private void Armageddon()
    {
        Vector origin = entities.GetOrigin(_player);

        Span<EntityHandle> nearby = stackalloc EntityHandle[ArmageddonPickCount];
        int count = world.EntitiesInSphere(origin, ArmageddonRadius, nearby);

        Span<EntityHandle> victims = stackalloc EntityHandle[ArmageddonPickCount];
        int picked = 0;
        for (int i = 0; i < count && picked < ArmageddonPickCount; i++)
        {
            EntityHandle victim = nearby[i];
            if (victim == EntityHandle.None || !entities.IsPlayer(victim))
                continue;

            // Not one of ours, not invulnerable (:18772): the plain number's equality, self among them.
            if (enginePlayers.GetTeam(victim) == enginePlayers.GetTeam(_player) || players.IsInvulnerable(victim))
                continue;

            // The box query's candidates, the squared distance the test that stands (:18775-18779).
            if ((origin - entities.GetOrigin(victim)).LengthSquared > ArmageddonRadius * ArmageddonRadius)
                continue;

            // The sight line (:18781-18790): centre to centre, MASK_SOLID_BRUSHONLY — what
            // <see cref="TraceMask.Brushes"/> answers. Short of whole on anything but the victim
            // himself, he is blocked (:18789).
            TraceResult tr = world.TraceLine(entities.GetWorldSpaceCenter(_player), entities.GetWorldSpaceCenter(victim),
                                             TraceMask.Brushes, _player);
            if (tr.Fraction < 1f && tr.Entity != victim)
                continue;

            victims[picked++] = victim;
        }

        if (picked == 0)
            return;

        // The pick slots, stock's nRandomPick (:18762): each round draws its index between 0 and its
        // turn (:18803); the end of the list moves over what the slot spent (:18821).
        Span<int> pick = stackalloc int[ArmageddonPickCount];
        for (int i = 0; i < ArmageddonPickCount; i++)
            pick[i] = i;

        float fDamage = 400f;                                       // the first pick's own (:18798)
        for (int i = picked - 1; i >= 0; i--)                       // :18800
        {
            int nRand = _random.RandomInt(0, i);                    // :18803
            EntityHandle victim = victims[pick[nRand]];             // :18804
            if (victim != EntityHandle.None)                        // :18805
            {
                using (scopes.Push(TFDamageCustom.TauntatkArmageddon, WeaponOf(), WeaponNameOf()))   // :18809
                    damage.ApplyAt(victim, _player, fDamage, DamageType.Plasma, default, origin);   // :18809

                fDamage /= 2;                                       // half again for the next pick (:18818)
                pick[nRand] = pick[i];                              // :18821
            }
        }
    }

    private TFWeaponId WeaponOf()
    {
        string name = WeaponNameOf();
        return name.Length > 0 ? scripts.Get(name)?.Id ?? TFWeaponId.None : TFWeaponId.None;
    }

    private string WeaponNameOf() => enginePlayers.GetActiveWeapon(_player);

    /// <summary>The victim's world box against the query's, corner pairs over each axis.</summary>
    private bool OverlapsBox(EntityHandle victim, Vector mins, Vector maxs)
    {
        (Vector lo, Vector hi) = entities.GetBounds(victim);
        Vector origin = entities.GetOrigin(victim);

        return origin.X + lo.X <= maxs.X && origin.X + hi.X >= mins.X
            && origin.Y + lo.Y <= maxs.Y && origin.Y + hi.Y >= mins.Y
            && origin.Z + lo.Z <= maxs.Z && origin.Z + hi.Z >= mins.Z;
    }

    private static bool SceneIs(string scene, string expected) => string.Equals(scene, expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>Half the box's diagonal: the sphere that cannot miss anything the box holds, the sweep's candidate cut.</summary>
    private static float BoxCornerRadius => BoxHalfExtent * MathF.Sqrt(3f);
}
