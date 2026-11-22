using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Medigun;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Tests.Weapons;

/// <summary>The crit convars as a fact sets them.</summary>
internal sealed class FixedCritSettings : ITFCritSettings
{
    public TFCritSettings Current { get; set; } = new();
}

/// <summary>The machine's arms as a recording: what a shot did, and no animations beyond the reload a fact gives the viewmodel.</summary>
internal sealed class RecordingArms : IWeaponArms
{
    public readonly List<(string Weapon, int Pellets, float Spread, ShotDamage Shot, PelletPattern? Fixed)> Bullets =
        new();
    public readonly List<(string Weapon, ShotDamage Shot)> Swings = new();

    /// <summary>The swung-over arms call: the row, the reach and hull bounds it
    /// was handed, and the script's two hit sounds.</summary>
    public readonly List<(string Weapon, float Range, float HullHalfExtent, string HitWorldSound,
                          string HitPlayerSound)> ArmSwings = new();
    public readonly List<string> Sounds = new();
    public readonly List<string> Launches = new();
    public readonly List<(int Fov, float Time)> Fovs = new();
    public readonly List<(string Weapon, string Particle)> Flashes = new();

    /// <summary>The reload animation's length; 0 is no such sequence (the viewmodels' default).</summary>
    public float ReloadSeconds { get; set; }

    /// <summary>
    /// The animation names a fact gives the arms (the c_heavy_arms table for
    /// the minigun's facts), so a <see cref="WeaponAnim"/> resolves to a
    /// sequence number and the viewmodel's Sequence/AnimationParity become
    /// observable. An anim outside the table resolves as none, as before.
    /// </summary>
    public readonly Dictionary<WeaponAnim, int> Sequences = new();

    public void Sound(string soundScript) => Sounds.Add(soundScript);

    /// <summary>The pitched ask, recorded with its multiplier so a fact can read the rate off it.</summary>
    public void Sound(string soundScript, float pitchMultiplier) =>
        Sounds.Add($"{soundScript} @{Math.Round(pitchMultiplier, 3)}");
    public void FireBullets(in WeaponType type, int pellets, float spreadDegrees) =>
        throw new InvalidOperationException("the core fires a TF shot with its damage");
    public void FireBullets(in WeaponType type, int pellets, float spreadDegrees, ShotDamage shot) =>
        FireBullets(in type, pellets, spreadDegrees, shot, null);

    public void FireBullets(in WeaponType type, int pellets, float spreadDegrees, ShotDamage shot,
                            PelletPattern? fixedPellets) =>
        Bullets.Add((type.Name, pellets, spreadDegrees, shot, fixedPellets));
    public void MeleeSwing(in WeaponType type) => throw new InvalidOperationException("the core swings a TF weapon with its damage");
    public void MeleeSwing(in WeaponType type, ShotDamage shot) => Swings.Add((type.Name, shot));
    public void MeleeSwing(in WeaponType type, float range, in MeleeHull hull, string hitWorldSound,
                           string hitPlayerSound) =>
        ArmSwings.Add((type.Name, range, hull.HalfExtent, hitWorldSound, hitPlayerSound));
    public int SwingsShown { get; private set; }
    public void SwingShown() => SwingsShown++;
    public readonly List<SourceSharp.Shared.Anim.WeaponGestureTrigger> Gestures = new();

    public void GestureShown(SourceSharp.Shared.Anim.WeaponGestureTrigger trigger) => Gestures.Add(trigger);
    public void Launch(in WeaponType type, string projectile, float speed, bool thrown) => Launches.Add(projectile);
    public int LookupSequence(WeaponAnim anim)
        => Sequences.TryGetValue(anim, out int sequence) ? sequence : anim == WeaponAnim.Reload && ReloadSeconds > 0f ? 7 : -1;
    public float AnimDuration(WeaponAnim anim) => anim == WeaponAnim.Reload ? ReloadSeconds : 0f;
    public void SetFov(int fov, float time) => Fovs.Add((fov, time));
    public void MuzzleFlash(in WeaponType type, string particleEffect) => Flashes.Add((type.Name, particleEffect));

    /// <summary>How often the hands were asked for the next best weapon (the lunchbox throw's tail).</summary>
    public int NextBestSwitches;

    public bool SwitchToNextBest() { NextBestSwitches++; return true; }

    /// <summary>The carrier whose velocity a shove rewrites; null keeps the seam's none.</summary>
    public PlayerNetState? Carrier { get; set; }

    /// <summary>The aim a behaviour's shove reads; false keeps the seam's nothing.</summary>
    public bool AimAnswers { get; set; }
    public Vector AimEye { get; set; }
    public Vector AimForward { get; set; } = new(1f, 0f, 0f);

    bool IWeaponArms.Aim(out Vector eye, out Vector forward)
    {
        eye = AimEye;
        forward = AimForward;
        return AimAnswers;
    }
}

/// <summary>Records what the TF effects were asked to do.</summary>
internal sealed class RecordingEffects : ITFWeaponEffects
{
    public readonly List<(string Weapon, ShotDamage Shot, float Charge)> Projectiles = new();
    public int Detonations;

    /// <summary>The shove asks, the weapon's name and the presser: the Shortstop's facts read them.</summary>
    public List<(string Weapon, int Owner)> Shoves = new();
    public readonly List<(string Weapon, ShotDamage Shot)> FlameTicks = new();
    public readonly List<string> AirBlasts = new();
    public readonly List<bool> Heals = new();
    public readonly List<bool> Zooms = new();
    public readonly List<int> ClassSkills = new();
    public readonly List<string> SelectLasts = new();
    public readonly List<(string Weapon, ShotDamage Shot, SwingTrace Trace)> Swings = new();
    public readonly List<(int Projectiles, bool Critical)> Sweeps = new();
    public readonly List<int> Rings = new();
    public int RearmedCount;
    public int TracerCount;
    public readonly List<string> Speeches = new();
    public int SceneClears;
    public readonly List<string> Firings = new();
    public readonly List<(string Weapon, int Mode)> LunchboxDrops = new();
    public int LunchboxEats;
    public readonly List<(int Owner, int BuffType)> Buffs = new();
    public readonly List<string> ShockAttacks = new();

    /// <summary>
    /// The server's lunchbox side the eat is handed to, the way
    /// <see cref="TFWeaponEffects"/> hands it: a real-press fact sets it and
    /// the eat runs the whole taunt chain behind the count. Unset keeps the
    /// record-only shape every counting fact reads.
    /// </summary>
    public ITFLunchboxEffects? LunchboxEffects;

    public void Projectile(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, float charge) => Projectiles.Add((script.Name, shot, charge));
    public void Detonate(in WeaponAttackContext attack, TFWeaponScript script) => Detonations++;
    public void Shove(in WeaponAttackContext attack, TFWeaponScript script) => Shoves.Add((script.Name, attack.Command.OwnerIndex));

    public void ShockAttack(in WeaponAttackContext attack, TFWeaponScript script) => ShockAttacks.Add(script.Name);
    public void Flames(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot) => FlameTicks.Add((script.Name, shot));
    public void AirBlast(in WeaponAttackContext attack, TFWeaponScript script) => AirBlasts.Add(script.Name);
    public readonly List<string> ExtinguishBeams = new();

    public bool ExtinguishBeam(in WeaponAttackContext attack, TFWeaponScript script)
    {
        ExtinguishBeams.Add(script.Name);
        return true;
    }
    public void Heal(in WeaponAttackContext attack, TFWeaponScript script, bool held) => Heals.Add(held);
    public void Zoom(in WeaponAttackContext attack, TFWeaponScript script, bool zoomed) => Zooms.Add(zoomed);
    public bool ClassSpecialSkill(in WeaponAttackContext attack)
    {
        ClassSkills.Add(attack.Command.OwnerIndex);
        return true;
    }

    public void EndClassSpecialSkill(in WeaponAttackContext attack) => ClassSkillEnds.Add(attack.Command.OwnerIndex);

    public readonly List<int> ClassSkillEnds = new();

    public void SelectLast(in WeaponAttackContext attack, TFWeaponScript script) => SelectLasts.Add(script.Name);
    public readonly List<float> SwingStarts = new();
    public void MeleeSwingStarted(in WeaponAttackContext attack, float swingRange) => SwingStarts.Add(swingRange);
    public void Melee(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, in SwingTrace trace) => Swings.Add((script.Name, shot, trace));
    public void AttackEnemyProjectiles(in WeaponAttackContext attack, int projectiles, bool critical) => Sweeps.Add((projectiles, critical));
    public void RingOfFire(in WeaponAttackContext attack, int damage) => Rings.Add(damage);
    public void Rearmed(in WeaponAttackContext attack) => RearmedCount++;
    public void Tracer(in WeaponAttackContext attack, TFWeaponScript script) => TracerCount++;
    public void SpeakWeaponFire(in WeaponAttackContext attack, string concept) => Speeches.Add(concept);
    public void ClearWeaponFireScene(in WeaponAttackContext attack) => SceneClears++;
    public void FiringSpeech(in WeaponAttackContext attack) => Firings.Add("TLK_MINIGUN_FIREWEAPON");
    public void LunchboxEat(in WeaponAttackContext attack, TFWeaponScript script)
    {
        LunchboxEats++;
        LunchboxEffects?.LunchboxEat(in attack, script);
    }

    public void LunchboxDrop(in WeaponAttackContext attack, TFWeaponScript script, int mode) => LunchboxDrops.Add((script.Name, mode));

    public void DeployBuff(in WeaponAttackContext attack, int buffType) => Buffs.Add((attack.Command.OwnerIndex, buffType));

    public readonly List<(int Owner, int BuffType)> Horns = new();
    public readonly List<(int Owner, int BuffType)> Raises = new();

    public void BlowBuffHorn(in WeaponAttackContext attack, int buffType) => Horns.Add((attack.Command.OwnerIndex, buffType));
    public void RaiseBuffBanner(in WeaponAttackContext attack, int buffType) => Raises.Add((attack.Command.OwnerIndex, buffType));

    public readonly List<(int Owner, int BuffType)> Rages = new();

    public void UseRage(in WeaponAttackContext attack, int buffType) => Rages.Add((attack.Command.OwnerIndex, buffType));
}

/// <summary>What the builder's placement was asked, for the builder's facts.</summary>
internal sealed class RecordingBuilderPlacement : ITFBuilderPlacement
{
    public readonly List<string> Asked = new();

    public void StartPlacement(int ownerIndex) => Asked.Add($"start {ownerIndex}");
    public void StartAttachmentPlacement(int ownerIndex) => Asked.Add($"startattach {ownerIndex}");
    public void UpdatePlacement(int ownerIndex) => Asked.Add($"update {ownerIndex}");
    public bool Place(int ownerIndex) { Asked.Add($"place {ownerIndex}"); return true; }
    public void Rotate(int ownerIndex) => Asked.Add($"rotate {ownerIndex}");
    public void StopPlacement(int ownerIndex) => Asked.Add($"stop {ownerIndex}");
    public bool ArmBuilder(int ownerIndex) { Asked.Add($"arm {ownerIndex}"); return true; }
    public bool BeginOrder(SourceSharp.Abi.EntityHandle player, TFObjectType type, int mode)
    { Asked.Add($"order {player} {type} {mode}"); return true; }
}

/// <summary>What the sapper's use was asked, for the trigger's fact. The
/// wrench's hand-out keeps the server's shape: the last smack's outcome is
/// stored and named once, so the smack's sound ask reads one answer.</summary>
internal sealed class RecordingSapperState : ITFSapperState
{
    public readonly List<string> Asked = new();
    public readonly List<(int Owner, float Range, float Amount)> Melecs = new();

    /// <summary>What the next place answers; a fact turns it for the refused arm.</summary>
    public bool Places { get; set; } = true;

    private WrenchSmackResult _lastSmack;

    public bool PlaceSapper(int ownerIndex) { Asked.Add($"sapper {ownerIndex}"); return Places; }

    public bool FindSappable(int ownerIndex, out EntityHandle building, out Vector point)
    { Asked.Add($"findsappable {ownerIndex}"); building = EntityHandle.None; point = Vector.Zero; return false; }

    public WrenchSmackResult WrenchSmack(int ownerIndex, float range)
    {
        Asked.Add($"wrench {ownerIndex} {range}");
        _lastSmack = WrenchSmackResult.Useful;
        return WrenchSmackResult.Useful;
    }

    public string? TakeWrenchSound(int ownerIndex)
    {
        WrenchSmackResult met = _lastSmack;
        _lastSmack = WrenchSmackResult.None;
        return met switch
        {
            WrenchSmackResult.Useful => TFWrenchHook.SuccessSound,
            WrenchSmackResult.Futile => TFWrenchHook.FailureSound,
            _ => null,
        };
    }

    public bool MeleeSmack(int ownerIndex, float range, float amount)
    { Melecs.Add((ownerIndex, range, amount)); return true; }
}

/// <summary>What the Wrangler's triggers asked, in order: the seam recorded.</summary>
internal sealed class RecordingSentryWrangle : ITFSentryWrangle
{
    public readonly List<string> Asked = new();

    public void Aim(int ownerIndex) => Asked.Add($"aim {ownerIndex}");

    public void Fire(int ownerIndex) => Asked.Add($"fire {ownerIndex}");

    public void FireRockets(int ownerIndex) => Asked.Add($"rockets {ownerIndex}");

    public void Release(int ownerIndex) => Asked.Add($"release {ownerIndex}");
}

/// <summary>The predicted carrier a replay arm reads (the client's own).</summary>
internal sealed class FakePredictedData : ITFPlayerData
{
    public TFPlayerNetState? Carrier { get; set; }

    public TFPlayerNetState? Predicted(int playerIndex) => Carrier;

    public TFPlayerNetState? Networked(int playerIndex) => Carrier;
}

/// <summary>The round's setup gate as a fact sets it.</summary>
internal sealed class FakeRoundSetup : TF2.Shared.Rules.ITFRoundSetup
{
    public bool InSetupWithRoundTimer { get; set; }
}

/// <summary>Nobody has asked for the idle top-up: the wish the world
/// answers with until a fact hands its own.</summary>
internal sealed class NobodyWantsAutoReload : ITFAutoReload
{
    public bool Wants(int ownerIndex) => false;
}

/// <summary>No stored crits: the bucket the world answers with until a
/// fact hands its own.</summary>
internal sealed class EmptyRevengeCrits : ITFRevengeCrits
{
    public void Grant(EntityHandle owner) { }

    public void SpendOnShot(int ownerIndex) { }
}

/// <summary>
/// One side's TF weapons over the shipped scripts and the core machine: the
/// catalogue with the TF rows, the TF behaviours, the TF ammo, a fake carrier
/// and the recording arms. One player (entity 3) holding weapon entity 5.
/// </summary>
internal sealed class TFWeaponWorld
{
    public const int Owner = 3;
    public const int WeaponEntity = 5;

    public FakeOwners Owners { get; } = new();
    public FakeRoundSetup Round { get; } = new();
    public FixedCritSettings Settings { get; } = new();
    public WeaponMedigunChargeRateConVar ChargeRate { get; } = BindCharge("40");
    public TFUseFixedWeaponspreadsConVar Spread { get; } = BindSpread("0");
    public RecordingArms Arms { get; } = new();
    public RecordingEffects Effects { get; } = new();
    public RecordingBuilderPlacement Builder { get; } = new();
    public RecordingSapperState Sapper { get; } = new();

    /// <summary>The scripted engine the attribute hook asks for the owner's
    /// health (<c>Returns("Entities.GetHealth", …)</c> sets it for the panic
    /// arm's facts).</summary>
    public RecordingEngine Engine { get; } = new();

    /// <summary>What the Wrangler's triggers asked, for its facts.</summary>
    public RecordingSentryWrangle Wrangle { get; } = new();
    public RecordingLog Log { get; } = new();

    /// <summary>The airblast's ammunition convar, bound to its declared
    /// default; <see cref="SetBurstammo"/> turns it.</summary>
    public TFFlameThrowerBurstammoConVar Burstammo { get; } = Bind(BurstammoDefault);

    /// <summary>The replayed medigun half the world's behaviours ask: the
    /// server's silent answer, the replay facts handing their own.</summary>
    public TFServerMedigunPrediction Medigun { get; } = new();

    /// <summary>The idle top-up's wish: nobody's, until a fact hands its own
    /// (<c>autoReload:</c>) — the declared default answer.</summary>
    public NobodyWantsAutoReload AutoReload { get; } = new();

    /// <summary>The stored-crit bucket: empty, until a fact hands its own
    /// (<c>bucket:</c>) — the declared default answer.</summary>
    public EmptyRevengeCrits Revenge { get; } = new();

    public TFWeaponParts Parts { get; }
    public WeaponCatalog Catalog { get; }
    public WeaponBehaviours Behaviours { get; }
    public AmmoCatalog Ammo { get; }
    public WeaponLogic Machine { get; }

    public TFWeaponWorld(params ITFWeaponHook[] hooks) : this(fixedSpread: null, hooks: hooks) { }

    /// <summary>A world whose parts carry one sapper seam — the same instance
    /// the wrench's hook holds, as the side's registration hands one service
    /// to both roles.</summary>
    public TFWeaponWorld(ITFSapperState sapper, params ITFWeaponHook[] hooks)
        : this(null, null, null, null, null, null, sapper, hooks) { }

    /// <summary>A world over the extras set too (<see cref="WeaponFixtures.WithExtras"/>): the facts whose
    /// weapon lives in TestData/weapons-extra.</summary>
    public TFWeaponWorld(bool extras)
        : this(files: extras ? WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses()) : null,
               hooks: Array.Empty<ITFWeaponHook>()) { }

    /// <summary>A world whose parts carry a predicted-data fake (the client's replay arm).</summary>
    public TFWeaponWorld(FakePredictedData? data, params ITFWeaponHook[] hooks)
        : this(data: data, fixedSpread: null, hooks: hooks) { }

    /// <summary>A world whose parts carry a bound convar (the fixed spread's
    /// facts), or whose parts speak through a given effects — the real beam's
    /// facts hand the rig's <c>TFFlameEffects</c> so the press runs the real
    /// chain.</summary>
    public TFWeaponWorld(FakePredictedData? data = null, TFUseFixedWeaponspreadsConVar? fixedSpread = null,
                         ITFAutoReload? autoReload = null, TFClassTableTests.Files? files = null,
                         ITFRevengeCrits? bucket = null, ITFWeaponEffects? effects = null,
                         ITFSapperState? sapper = null, params ITFWeaponHook[] hooks)
    {
        var shipped = files ?? WeaponFixtures.ShippedWithClasses();
        var scripts = new TFWeaponScripts(Log, shipped);
        var classes = new TFClassTable(Log, shipped);
        var types = new TFWeaponTypes(scripts, classes, new ItemSchema(shipped, Log));
        var attributes = new AttributeHooks();
        var predicted = data ?? new FakePredictedData();
        var chain = TFWeaponHookChain.Of([new TFSwordHook(attributes),
                                          new TFAttributeWeaponHook(attributes, Engine, classes, predicted),
                                          new TFCritBoostHook(), new TFKingReloadHook(), .. hooks]);

        Parts = new TFWeaponParts(scripts, chain, new TFCritBuckets(), Settings, Owners, effects ?? Effects, Builder,
                                  predicted, Round, ChargeRate, fixedSpread ?? Spread,
                                  autoReload ?? AutoReload, Burstammo, Medigun, sapper ?? Sapper, Wrangle,
                                  bucket ?? Revenge);
        Catalog = new WeaponCatalog(() => [types], Log);
        Behaviours = new WeaponBehaviours(() => [new TFWeaponBehaviours(Parts)], Log);
        Ammo = new AmmoCatalog(() => [new TFAmmoTypes()], Log);
        Machine = new WeaponLogic(Arms, Catalog, Behaviours, Ammo);
        Owners.Classes[Owner] = TFClass.Scout;
    }

    public WeaponType Row(string weapon) => Catalog.For(weapon) ?? throw new InvalidOperationException($"no row {weapon}");

    /// <summary>The declared default (<c>tf_weapon_flamethrower.cpp:70</c>),
    /// which the world binds before any fact speaks.</summary>
    private const string BurstammoDefault = "20";

    private static TFFlameThrowerBurstammoConVar Bind(string value)
    {
        var convar = new TFFlameThrowerBurstammoConVar();
        convar.Bind(-1, "tf_flamethrower_burstammo", value);
        return convar;
    }

    /// <summary>The meter's build rate, bound to its declared default
    /// (<c>tf_weapon_medigun.cpp:1272</c>'s 40) until a fact binds otherwise.</summary>
    private static WeaponMedigunChargeRateConVar BindCharge(string value)
    {
        var convar = new WeaponMedigunChargeRateConVar();
        convar.Bind(-1, "weapon_medigun_charge_rate", value);
        return convar;
    }

    /// <summary>The fixed spread, bound to its declared default (the random
    /// sample) until a fact hands a set one (<c>fixedSpread:</c>).</summary>
    private static TFUseFixedWeaponspreadsConVar BindSpread(string value)
    {
        var convar = new TFUseFixedWeaponspreadsConVar();
        convar.Bind(-1, "tf_use_fixed_weaponspreads", value);
        return convar;
    }

    /// <summary>Sets the airblast's ammunition the way a registry's Bind would
    /// (the medigun rig's Set shape).</summary>
    public void SetBurstammo(string value) => Burstammo.Bind(-1, "tf_flamethrower_burstammo", value);

    public IWeaponBehaviour Behaviour(string weapon) =>
        Behaviours.For(Row(weapon).Behaviour) ?? throw new InvalidOperationException($"no behaviour for {weapon}");

    public (WeaponNetState Weapon, ViewModelNetState ViewModel) Armed(string weapon, int clip, int reserve)
    {
        var w = new WeaponNetState();
        w.BindLocal();
        var vm = new ViewModelNetState();
        vm.BindLocal();
        w.WeaponTypeId = Catalog.IndexOf(weapon);
        w.OwnerIndex = Owner;
        w.Clip = clip;
        w.Reserve = reserve;
        return (w, vm);
    }

    public static WeaponCommand Command(int seed) => new(seed, WeaponEntity, Owner);

    public void Run(WeaponNetState w, ViewModelNetState vm, int buttons, float now, int seed = 1, bool live = true) =>
        Machine.RunCommand(w, vm, buttons, now, live, Command(seed));

    public WeaponAttackContext Attack(WeaponNetState w, ViewModelNetState vm, float now, int seed = 1, bool live = true) =>
        new(Catalog.ByIndex(w.WeaponTypeId)!.Value, w, vm, now, live, Arms, Command(seed));

    /// <summary>A command seed whose gun roll draws in [lo, hi) for this world's weapon and owner.</summary>
    public static int GunSeed(int lo, int hi) => SeedDrawing(s => TFCritRoll.GunSeed(Command(s)), lo, hi);

    /// <summary>A command seed whose melee roll draws in [lo, hi).</summary>
    public static int MeleeSeed(int lo, int hi) => SeedDrawing(s => TFCritRoll.MeleeSeed(Command(s)), lo, hi);

    private static int SeedDrawing(Func<int, int> seedOf, int lo, int hi)
    {
        var roll = new TFCritRoll();
        for (int seed = 1; seed < 200_000; seed++)
        {
            int r = roll.Draw(seedOf(seed));
            if (r >= lo && r < hi)
                return seed;
        }
        throw new InvalidOperationException($"no seed draws in [{lo}, {hi})");
    }
}
