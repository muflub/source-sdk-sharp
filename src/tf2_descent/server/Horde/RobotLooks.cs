using SourceSharp.Abi;
using SourceSharp.Ai;

using TF2.Descent.Rules.Robots;

namespace TF2.Descent.Server.Horde;

/// <summary>The TF player's standing hull, which a robot body scales (HO0, 3.11 row 1).</summary>
public static class RobotHull
{
    public static Vector Mins => new(-24f, -24f, 0f);

    public static Vector Maxs => new(24f, 24f, 82f);
}

/// <summary>The engine's vectors and the horde's.</summary>
public static class HordeVectors
{
    public static AiVector ToAi(Vector v) => new(v.X, v.Y, v.Z);

    public static Vector ToEngine(AiVector v) => new(v.X, v.Y, v.Z);
}

/// <summary>
/// A hit's engine damage bits as a Descent damage type (damage_types.json): fire
/// for burning (TF's afterburn is <see cref="DamageType.Burn"/>, its ignite
/// <see cref="DamageType.Plasma"/>), blast, shock for shock and energy beams,
/// and bullet for everything else, melee included (families.json gives melee
/// weapons bullet). Fire first: a burning blast (a flare's detonation) is fire.
/// <see cref="DamageType.SlowBurn"/> is not fire: TF reuses it as "use the
/// distance mod" on rockets and bullets (TFDamageDefs.UseDistanceMod), as it
/// reuses radiation as "half falloff".
/// </summary>
public static class RobotDamageKinds
{
    public const string Fire = "fire";
    public const string Blast = "blast";
    public const string Bullet = "bullet";
    public const string Shock = "shock";

    public static string Of(DamageType bits) =>
        (bits & (DamageType.Burn | DamageType.Plasma)) != 0 ? Fire
        : (bits & DamageType.Blast) != 0 ? Blast
        : (bits & (DamageType.Shock | DamageType.EnergyBeam)) != 0 ? Shock
        : Bullet;

    /// <summary>The engine bits a hit of Descent damage <paramref name="type"/> is dealt with (a status's tick): <see cref="Of"/> reads them back as the type.</summary>
    public static DamageType BitsOf(string type) => type switch
    {
        Fire => DamageType.Burn,
        Blast => DamageType.Blast,
        Shock => DamageType.Shock,
        _ => DamageType.Bullet,
    };
}

/// <summary>
/// What a robot looks like: its body model, the weapon model it holds, and the
/// sequences it stands and runs with. A small table in the server for now; its
/// place later is the rules' robots.json (a model and a weapon column a
/// template), once something other than the server needs to know.
///
/// <para>
/// Models (HO0, 3.11 row 11): a robot model for each of the nine classes; a
/// giant model for scout, soldier, pyro, demoman and heavy. A giant or a boss
/// of another class (the giant medic) wears the ordinary model, scaled.
/// </para>
/// </summary>
public static class RobotLooks
{
    public const string ScoutModel = "models/bots/scout/bot_scout.mdl";
    public const string SoldierModel = "models/bots/soldier/bot_soldier.mdl";
    public const string PyroModel = "models/bots/pyro/bot_pyro.mdl";
    public const string DemomanModel = "models/bots/demo/bot_demo.mdl";
    public const string HeavyModel = "models/bots/heavy/bot_heavy.mdl";
    public const string EngineerModel = "models/bots/engineer/bot_engineer.mdl";
    public const string MedicModel = "models/bots/medic/bot_medic.mdl";
    public const string SniperModel = "models/bots/sniper/bot_sniper.mdl";
    public const string SpyModel = "models/bots/spy/bot_spy.mdl";

    public const string GiantScoutModel = "models/bots/scout_boss/bot_scout_boss.mdl";
    public const string GiantSoldierModel = "models/bots/soldier_boss/bot_soldier_boss.mdl";
    public const string GiantPyroModel = "models/bots/pyro_boss/bot_pyro_boss.mdl";
    public const string GiantDemomanModel = "models/bots/demo_boss/bot_demo_boss.mdl";
    public const string GiantHeavyModel = "models/bots/heavy_boss/bot_heavy_boss.mdl";

    /// <summary>MvM's sentry buster (stock's <c>bot_sentry_buster.mdl</c>, the demoman's skeleton): a template that fights with the buster's blast wears it.</summary>
    public const string SentryBusterModel = "models/bots/demo/bot_sentry_buster.mdl";

    /// <summary>MvM's Tank (HO13; mine plan D11): its body (the model's own treads and drill parts are not driven).</summary>
    public const string TankModel = "models/bots/boss_bot/boss_tank.mdl";

    /// <summary>The Tank's one sequence: its treads rolling.</summary>
    public const string TankSequence = "movement";

    /// <summary>The attack row a sentry buster fights with (robot_attacks.json).</summary>
    public const string SentryBusterAttack = "sentry_buster";

    public const string RocketLauncher = "models/weapons/c_models/c_rocketlauncher/c_rocketlauncher.mdl";
    public const string GrenadeLauncher = "models/weapons/c_models/c_grenadelauncher/c_grenadelauncher.mdl";
    public const string Flamethrower = "models/weapons/c_models/c_flamethrower/c_flamethrower.mdl";
    public const string Scattergun = "models/weapons/c_models/c_scattergun.mdl";
    public const string Minigun = "models/weapons/c_models/c_minigun/c_minigun.mdl";
    public const string Shotgun = "models/weapons/c_models/c_shotgun/c_shotgun.mdl";
    public const string SyringeGun = "models/weapons/c_models/c_syringegun/c_syringegun.mdl";
    public const string SniperRifle = "models/weapons/c_models/c_sniperrifle/c_sniperrifle.mdl";
    public const string Knife = "models/weapons/c_models/c_knife/c_knife.mdl";

    /// <summary>
    /// The BLU skin of a robot model, its held weapon and its ragdoll: robots are
    /// BLU, MvM's invaders (<see cref="Mode.DescentMode.RobotTeam"/>). TF2's rule
    /// for a player and a weapon: RED 0, BLU 1 (<c>C_TFPlayer::GetSkin</c>,
    /// c_tf_player.cpp:7808-7821; <c>CTFWeaponBase::GetSkin</c>,
    /// tf_weaponbase.cpp:4925-4937). Every bot model has four families (RED,
    /// BLU and their invulnerable sheens), the giants' too; a model with one
    /// (the sentry buster, the shotgun) draws it, as the studio renderer takes
    /// family 0 for a skin past its last.
    /// </summary>
    public const int BluSkin = 1;

    /// <summary>
    /// The skin a robot body is drawn in (<see cref="BluSkin"/>), but the Tank's:
    /// stock's <c>CTFTankBoss</c> sets none, so its BLU tank is family 0 (its
    /// second is the <c>tankbody2_final</c> look).
    /// </summary>
    public static int BodySkin(string model) => model == TankModel ? 0 : BluSkin;

    /// <summary>
    /// Whether the template's death leaves a ragdoll (<see cref="RobotRagdolls"/>):
    /// every robot's does, as MvM's robots leave a <c>tf_ragdoll</c> (MvM never
    /// gibs a robot, tf_player.cpp:10925-10927), but two whose death is not a
    /// body: the Tank (stock's <c>CTFTankBoss</c> blows apart; it is no player
    /// and leaves no ragdoll) and the sentry buster, whose death is its blast
    /// (<c>ChampionDeaths</c>, <c>RobotAttacks.Blast</c>).
    /// </summary>
    public static bool LeavesRagdoll(RobotTemplate t) => t.Class != "tank" && t.Attack != SentryBusterAttack;

    /// <summary>Whether the template's size class is drawn with the giant model where its class has one.</summary>
    public static bool IsBig(RobotTemplate t) => t.Size is "giant" or "boss";

    public static string BodyModel(RobotTemplate t) => t.Attack == SentryBusterAttack ? SentryBusterModel : (t.Class, IsBig(t)) switch
    {
        ("tank", _) => TankModel,
        ("scout", true) => GiantScoutModel,
        ("soldier", true) => GiantSoldierModel,
        ("pyro", true) => GiantPyroModel,
        ("demoman", true) => GiantDemomanModel,
        ("heavy", true) => GiantHeavyModel,
        ("scout", _) => ScoutModel,
        ("soldier", _) => SoldierModel,
        ("pyro", _) => PyroModel,
        ("demoman", _) => DemomanModel,
        ("heavy", _) => HeavyModel,
        ("engineer", _) => EngineerModel,
        ("medic", _) => MedicModel,
        ("sniper", _) => SniperModel,
        ("spy", _) => SpyModel,
        _ => throw new ArgumentException($"robot template '{t.Id}' has class '{t.Class}', which has no robot model"),
    };

    /// <summary>The weapon a template holds, by its family; null for bare fists (a melee heavy).</summary>
    public static string? WeaponModel(RobotTemplate t) => t.Family switch
    {
        "rocket_launcher" => RocketLauncher,
        "grenade_launcher" => GrenadeLauncher,
        "flamethrower" => Flamethrower,
        "scattergun" => Scattergun,
        "minigun" => Minigun,
        "shotgun" => Shotgun,
        "syringe_gun" => SyringeGun,
        "sniper_rifle" => SniperRifle,
        "melee" => t.Class == "spy" ? Knife : null,   // a melee heavy's fists, and the Tank's nothing
        _ => throw new ArgumentException($"robot template '{t.Id}' holds family '{t.Family}', which has no robot weapon model"),
    };

    /// <summary>The player animations' standing sequence for what the template holds (plan 3.11 row 2: the robot models carry the player's).</summary>
    public static string IdleSequence(RobotTemplate t) => t.Class == "tank" ? TankSequence : t.Family == "melee" ? "stand_MELEE" : "stand_PRIMARY";

    /// <summary>The running sequence for what the template holds.</summary>
    public static string RunSequence(RobotTemplate t) => t.Class == "tank" ? TankSequence : t.Family == "melee" ? "run_MELEE" : "run_PRIMARY";

    /// <summary>
    /// The airborne loop for what the template holds (HO3): every robot model
    /// has <c>airwalk_PRIMARY</c> and <c>airwalk_MELEE</c> but the Spy, which
    /// has no primary (its lookup answers none and the body keeps its gait's
    /// last sequence). Only the Scout, Soldier and Medic have a jump float.
    /// </summary>
    public static string AirSequence(RobotTemplate t) => t.Class == "tank" ? TankSequence : t.Family == "melee" ? "airwalk_MELEE" : "airwalk_PRIMARY";
}
