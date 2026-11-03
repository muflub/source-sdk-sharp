using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Tests;

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
    public void FireBullets(in WeaponType type, int pellets, float spreadDegrees) =>
        throw new InvalidOperationException("the core fires a TF shot with its damage");
    public void FireBullets(in WeaponType type, int pellets, float spreadDegrees, ShotDamage shot) =>
        FireBullets(in type, pellets, spreadDegrees, shot, null);

    public void FireBullets(in WeaponType type, int pellets, float spreadDegrees, ShotDamage shot,
                            PelletPattern? fixedPellets) =>
        Bullets.Add((type.Name, pellets, spreadDegrees, shot, fixedPellets));
    public void MeleeSwing(in WeaponType type) => throw new InvalidOperationException("the core swings a TF weapon with its damage");
    public void MeleeSwing(in WeaponType type, ShotDamage shot) => Swings.Add((type.Name, shot));
    public void Launch(in WeaponType type, string projectile, float speed, bool thrown) => Launches.Add(projectile);
    public int LookupSequence(WeaponAnim anim)
        => Sequences.TryGetValue(anim, out int sequence) ? sequence : anim == WeaponAnim.Reload && ReloadSeconds > 0f ? 7 : -1;
    public float AnimDuration(WeaponAnim anim) => anim == WeaponAnim.Reload ? ReloadSeconds : 0f;
    public void SetFov(int fov, float time) => Fovs.Add((fov, time));
    public void MuzzleFlash(in WeaponType type, string particleEffect) => Flashes.Add((type.Name, particleEffect));
}

/// <summary>Records what the TF effects were asked to do.</summary>
internal sealed class RecordingEffects : ITFWeaponEffects
{
    public readonly List<(string Weapon, ShotDamage Shot, float Charge)> Projectiles = new();
    public int Detonations;
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
    public readonly List<string> Speeches = new();
    public int SceneClears;
    public readonly List<string> Firings = new();

    public void Projectile(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, float charge) => Projectiles.Add((script.Name, shot, charge));
    public void Detonate(in WeaponAttackContext attack, TFWeaponScript script) => Detonations++;
    public void Flames(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot) => FlameTicks.Add((script.Name, shot));
    public void AirBlast(in WeaponAttackContext attack, TFWeaponScript script) => AirBlasts.Add(script.Name);
    public void Heal(in WeaponAttackContext attack, TFWeaponScript script, bool held) => Heals.Add(held);
    public void Zoom(in WeaponAttackContext attack, TFWeaponScript script, bool zoomed) => Zooms.Add(zoomed);
    public bool ClassSpecialSkill(in WeaponAttackContext attack)
    {
        ClassSkills.Add(attack.Command.OwnerIndex);
        return true;
    }

    public void SelectLast(in WeaponAttackContext attack, TFWeaponScript script) => SelectLasts.Add(script.Name);
    public void Melee(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, in SwingTrace trace) => Swings.Add((script.Name, shot, trace));
    public void AttackEnemyProjectiles(in WeaponAttackContext attack, int projectiles, bool critical) => Sweeps.Add((projectiles, critical));
    public void RingOfFire(in WeaponAttackContext attack, int damage) => Rings.Add(damage);
    public void Rearmed(in WeaponAttackContext attack) => RearmedCount++;
    public void SpeakWeaponFire(in WeaponAttackContext attack, string concept) => Speeches.Add(concept);
    public void ClearWeaponFireScene(in WeaponAttackContext attack) => SceneClears++;
    public void FiringSpeech(in WeaponAttackContext attack) => Firings.Add("TLK_MINIGUN_FIREWEAPON");
}

/// <summary>What the builder's placement was asked, for the builder's facts.</summary>
internal sealed class RecordingBuilderPlacement : ITFBuilderPlacement
{
    public readonly List<string> Asked = new();

    public void StartPlacement(int ownerIndex) => Asked.Add($"start {ownerIndex}");
    public void UpdatePlacement(int ownerIndex) => Asked.Add($"update {ownerIndex}");
    public bool Place(int ownerIndex) { Asked.Add($"place {ownerIndex}"); return true; }
    public void Rotate(int ownerIndex) => Asked.Add($"rotate {ownerIndex}");
    public void StopPlacement(int ownerIndex) => Asked.Add($"stop {ownerIndex}");
    public bool ArmBuilder(int ownerIndex) { Asked.Add($"arm {ownerIndex}"); return true; }
    public bool BeginOrder(SourceSharp.Abi.EntityHandle player, TFObjectType type, int mode)
    { Asked.Add($"order {player} {type} {mode}"); return true; }
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
    public RecordingArms Arms { get; } = new();
    public RecordingEffects Effects { get; } = new();
    public RecordingBuilderPlacement Builder { get; } = new();
    public RecordingLog Log { get; } = new();

    /// <summary>The airblast's ammunition convar, bound to its declared
    /// default; <see cref="SetBurstammo"/> turns it.</summary>
    public TFFlameThrowerBurstammoConVar Burstammo { get; } = Bind(BurstammoDefault);

    public TFWeaponParts Parts { get; }
    public WeaponCatalog Catalog { get; }
    public WeaponBehaviours Behaviours { get; }
    public AmmoCatalog Ammo { get; }
    public WeaponLogic Machine { get; }

    public TFWeaponWorld(params ITFWeaponHook[] hooks) : this(null, null, null, hooks) { }

    /// <summary>A world whose parts carry a predicted-data fake (the client's replay arm).</summary>
    public TFWeaponWorld(FakePredictedData? data, params ITFWeaponHook[] hooks) : this(data, null, null, hooks) { }

    /// <summary>A world whose parts carry a bound convar (the fixed spread's facts).</summary>
    public TFWeaponWorld(FakePredictedData? data = null, TFUseFixedWeaponspreadsConVar? fixedSpread = null,
                         ITFAutoReload? autoReload = null, params ITFWeaponHook[] hooks)
    {
        var files = WeaponFixtures.ShippedWithClasses();
        var scripts = new TFWeaponScripts(Log, files);
        var types = new TFWeaponTypes(scripts, new TFClassTable(Log, files), new ItemSchema(files, Log));
        var chain = TFWeaponHookChain.Of([new TFAttributeWeaponHook(new AttributeHooks()), new TFCritBoostHook(),
                                          new TFKingReloadHook(), .. hooks]);

        Parts = new TFWeaponParts(scripts, chain, new TFCritBuckets(), Settings, Owners, Effects, Builder, data,
                                  Round, fixedSpread: fixedSpread, autoReload: autoReload,
                                  flamethrowerBurstammo: Burstammo);
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
