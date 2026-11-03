using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Tests.Entities;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The server's effects dispatcher: each shared-behaviour effect call reaches
/// the one seam that owns it, with its arguments, and a seam nobody owns does
/// nothing.
/// </summary>
public sealed class TFWeaponEffectsTests
{
    private sealed class Seams : ITFProjectileEffects, ITFFlameEffects, ITFHealEffects, ITFZoomEffects,
                                 ITFItemSwitchEffects
    {
        public readonly List<string> Calls = new();

        public void Projectile(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, float charge)
            => Calls.Add($"projectile {script.Name} {shot.Damage} {shot.Critical} {charge}");

        public void Detonate(in WeaponAttackContext attack, TFWeaponScript script) => Calls.Add($"detonate {script.Name}");

        public void Flames(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot)
            => Calls.Add($"flames {script.Name} {shot.Damage} {shot.Critical}");

        public void AirBlast(in WeaponAttackContext attack, TFWeaponScript script) => Calls.Add($"airblast {script.Name}");

        public void Heal(in WeaponAttackContext attack, TFWeaponScript script, bool held) => Calls.Add($"heal {script.Name} {held}");

        public void Zoom(in WeaponAttackContext attack, TFWeaponScript script, bool zoomed) => Calls.Add($"zoom {script.Name} {zoomed}");

        public void SelectLast(in WeaponAttackContext attack, TFWeaponScript script) => Calls.Add($"selectlast {script.Name}");
    }

    private static readonly WeaponAttackContext Attack = default;

    [Fact]
    public void A_projectile_and_a_detonation_reach_the_projectile_seam()
    {
        var seams = new Seams();
        var effects = new TFWeaponEffects(projectiles: seams);
        var launcher = WeaponFixtures.Parse("tf_weapon_pipebomblauncher");

        effects.Projectile(in Attack, launcher, new ShotDamage(120f, Critical: true), 2.5f);
        effects.Detonate(in Attack, launcher);

        Assert.Equal(["projectile tf_weapon_pipebomblauncher 120 True 2.5", "detonate tf_weapon_pipebomblauncher"], seams.Calls);
    }

    [Fact]
    public void Flames_heal_and_zoom_each_reach_their_own_seam()
    {
        var seams = new Seams();
        var effects = new TFWeaponEffects(flames: seams, heal: seams, zoom: seams);

        effects.Flames(in Attack, WeaponFixtures.Parse("tf_weapon_flamethrower"), new ShotDamage(6.8f));
        effects.Heal(in Attack, WeaponFixtures.Parse("tf_weapon_medigun"), held: true);
        effects.Zoom(in Attack, WeaponFixtures.Parse("tf_weapon_sniperrifle"), zoomed: false);

        Assert.Equal(["flames tf_weapon_flamethrower 6.8 False", "heal tf_weapon_medigun True", "zoom tf_weapon_sniperrifle False"],
                     seams.Calls);
    }

    [Fact]
    public void The_PDAs_item_switch_reaches_the_item_switch_seam()
    {
        var seams = new Seams();
        var effects = new TFWeaponEffects(itemSwitch: seams);

        effects.SelectLast(in Attack, WeaponFixtures.Parse("tf_weapon_pda_spy"));

        Assert.Equal(["selectlast tf_weapon_pda_spy"], seams.Calls);
    }

    [Fact]
    public void A_seam_nobody_owns_does_nothing()
    {
        var seams = new Seams();
        var effects = new TFWeaponEffects(heal: seams);   // only the medigun's seam is owned
        var rifle = WeaponFixtures.Parse("tf_weapon_sniperrifle");

        effects.Projectile(in Attack, rifle, new ShotDamage(1f), 0f);
        effects.Detonate(in Attack, rifle);
        effects.Flames(in Attack, rifle, new ShotDamage(1f));
        effects.Zoom(in Attack, rifle, zoomed: true);
        effects.SelectLast(in Attack, rifle);

        Assert.Empty(seams.Calls);
    }

    //-------------------------------------------------------------------------
    // The speech arms (CTFPlayer::SpeakWeaponFire and ClearWeaponFireScene,
    // tf_player.cpp:20158-20188; the firing gate at tf_weapon_minigun.cpp:336).
    //-------------------------------------------------------------------------

    private static WeaponAttackContext At(float now) =>
        new(default, default!, default!, now, EffectsLive: true, new RecordingArms(), new WeaponCommand(1, 5, 3));

    private static ObjectiveRig SpeechRig(out EntityHandle speaker)
    {
        var rig = new ObjectiveRig();
        rig.Engine.Returns("Players.IsAlive", true);
        speaker = new EntityHandle(0x0001_0000u | 3);
        rig.Engine.Returns("Players.GetHandle", speaker);
        return rig;
    }

    /// <summary>
    /// The plain fire ask (the empty concept) waits on the scene clock, which
    /// the five-second arming and the scene's own length keep shut;
    /// <c>ClearWeaponFireScene</c> opens it again (:20162, :20185-20188).
    /// </summary>
    [Fact]
    public void The_plain_fire_ask_waits_for_the_scene_and_the_clear_lets_it_in()
    {
        var rig = SpeechRig(out EntityHandle speaker);
        var effects = new TFWeaponEffects(speech: rig.Speech);

        effects.SpeakWeaponFire(At(10f), "");
        Assert.Equal("TLK_FIREWEAPON", rig.ConceptRules.Asked[0].Concept);

        effects.SpeakWeaponFire(At(10.2f), "");          // clock armed to 15
        Assert.Single(rig.ConceptRules.Asked);

        effects.ClearWeaponFireScene(At(10.2f));
        effects.SpeakWeaponFire(At(10.3f), "");
        Assert.Equal(2, rig.ConceptRules.Asked.Count);
        Assert.Equal("TLK_FIREWEAPON", rig.ConceptRules.Asked[1].Concept);

        // The ask sounded for the owner named by the command.
        EngineCall spoken = rig.Engine.AllOf("Sound.EntityEmitSound").First();
        Assert.Equal(speaker, spoken.ArgAs<EntityHandle>(0));
        Assert.Equal("vo/concept.ogg", spoken.ArgAs<string>(1));
    }

    /// <summary>
    /// A named concept — the minigun's wind and fire lines — ignores the
    /// clock and speaks every time (:20164-20178).
    /// </summary>
    [Fact]
    public void A_named_concept_ignores_the_scene_clock()
    {
        var rig = SpeechRig(out _);
        var effects = new TFWeaponEffects(speech: rig.Speech);

        effects.SpeakWeaponFire(At(10f), "TLK_WINDMINIGUN");
        effects.SpeakWeaponFire(At(10.1f), "TLK_WINDMINIGUN");
        effects.SpeakWeaponFire(At(10.2f), "TLK_FIREMINIGUN");

        Assert.Equal(["TLK_WINDMINIGUN", "TLK_WINDMINIGUN", "TLK_FIREMINIGUN"],
                     [.. rig.ConceptRules.Asked.Select(a => a.Concept)]);
    }

    /// <summary>
    /// The firing concept's own gate (:336-341): the first shot speaks, the
    /// next five seconds wait, and the ask rides its own clock beside the
    /// scene's.
    /// </summary>
    [Fact]
    public void The_firing_concept_speaks_once_across_its_five_seconds()
    {
        var rig = SpeechRig(out _);
        var effects = new TFWeaponEffects(speech: rig.Speech);

        effects.FiringSpeech(At(10f));
        Assert.Single(rig.ConceptRules.Asked);
        Assert.Equal("TLK_MINIGUN_FIREWEAPON", rig.ConceptRules.Asked[0].Concept);

        effects.FiringSpeech(At(10.2f));                 // inside the window
        Assert.Single(rig.ConceptRules.Asked);

        effects.FiringSpeech(At(15.1f));                 // the clock has passed
        Assert.Equal(2, rig.ConceptRules.Asked.Count);

        // The scene clock is the other arm's; the firing gate never touched it.
        effects.SpeakWeaponFire(At(15.2f), "");
        Assert.Equal(3, rig.ConceptRules.Asked.Count);
    }

    /// <summary>
    /// The firing ask carries its held weapon's words beside the concept
    /// (<c>CTFPlayer::ModifyOrAppendCriteria</c>, tf_player.cpp:19212+): the
    /// classname in hand, the firing duration to one decimal
    /// (<c>GetFiringDuration</c>, tf_weapon_minigun.h:97), and the roll the
    /// percent-chance criteria read.
    /// </summary>
    [Fact]
    public void The_firing_ask_carries_the_held_weapon_words()
    {
        var rig = SpeechRig(out _);
        rig.Random.Next = 80;
        var effects = new TFWeaponEffects(speech: rig.Speech);

        var weaponState = new WeaponNetState();
        weaponState.BindLocal();
        weaponState.BehaviourMeter = 10f - TFMinigunBehaviour.SpinUpTime - 5f;

        var attack = new WeaponAttackContext(
            new WeaponType("tf_weapon_minigun", 0.1f), weaponState,
            default!, 10f, EffectsLive: true, new RecordingArms(), new WeaponCommand(1, 5, 3));

        effects.FiringSpeech(in attack);

        Assert.Equal("TLK_MINIGUN_FIREWEAPON", rig.ConceptRules.Asked[0].Concept);
        Assert.Contains(("playerweapon", "tf_weapon_minigun"), rig.ConceptRules.Words);
        Assert.Contains(("minigunfiretime", "5.0"), rig.ConceptRules.Words);
        Assert.Contains(("randomnum", "80"), rig.ConceptRules.Words);
    }
}
