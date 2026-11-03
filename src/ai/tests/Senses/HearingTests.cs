using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Senses;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.Senses;

/// <summary>
/// Hearing (plan_hl2mp_ai §4.2): a bot hears a sound exactly when it is in
/// the sound's recipient set, never its own, with the kind and the weapon the
/// weapon table names; its heard place is near the truth, nearer with
/// Alertness.
/// </summary>
public sealed class HearingTests
{
    private static readonly ActorId Bot = new(3, 1);
    private static readonly ActorId Shooter = new(2, 0x4000_0001);
    private static readonly SoundClassifier Classifier = new(WeaponCatalog.Stock());

    private static bool Hear(string name, int owner, int[] recipients, out AiEvent e, int alertness = 5, AiVector? at = null)
    {
        (SoundKind kind, int weapon) = Classifier.Classify(name);
        var sound = new EmittedSound(new Vector(1000, 0, 0), 140, name, owner, recipients);
        return SoundTap.TryHear(Bot, at ?? AiVector.Zero, alertness, in sound, kind, weapon, Shooter, new Tick(5), new AiRandom(9), out e);
    }

    [Fact]
    public void AGunshotWhoseRecipientsExcludeTheBotIsNotHeard()
        => Assert.False(Hear("Weapon_SMG1.Single", 2, [1, 4], out _));

    [Fact]
    public void AGunshotWhoseRecipientsIncludeTheBotIsHeardAsAnSmg1Shot()
    {
        Assert.True(Hear("Weapon_SMG1.Single", 2, [1, 3, 4], out AiEvent e));
        Assert.Equal(AiEventKind.SoundHeard, e.Kind);
        Assert.Equal(SoundKind.Gunfire, (SoundKind)e.Code);
        Assert.Equal(WeaponCatalog.Stock().IndexOf("weapon_smg1"), e.Code2);
        Assert.Equal(Shooter, e.Other);
        Assert.Equal(Bot, e.Bot);
    }

    [Fact]
    public void ABotsOwnFootstepsAreNeverHeard()
    {
        Assert.False(Hear("Concrete.StepLeft", 3, [1, 2, 3], out _));
        Assert.True(Hear("Concrete.StepLeft", 2, [1, 2, 3], out AiEvent e), "the control: another player's step is");
        Assert.Equal(SoundKind.Footstep, (SoundKind)e.Code);
    }

    /// <summary>
    /// The server sends a step as the script's wave (CHL2MP_Player::PlayStepSound
    /// emits params.soundname), so the wave is what the tap classifies.
    /// </summary>
    [Fact]
    public void TheStepsWaveIsAFootstep()
        => Assert.Equal((SoundKind.Footstep, -1), Classifier.Classify("npc/footsteps/hardboot_generic1.wav"));

    /// <summary>
    /// A step the clients in the walker's PVS make themselves comes with the
    /// PVS's players as its recipients: a bot among them hears it, a bot
    /// outside the PVS hears it only as the server's emit (the PAS less the PVS).
    /// </summary>
    [Fact]
    public void ABotInTheWalkersPvsHearsTheStepItsClientMakes()
    {
        var step = new EmittedSound(new Vector(300, 0, 0), 70, "npc/footsteps/hardboot_generic1.wav", 2, [2, 3]);
        Assert.True(SoundTap.TryHear(Bot, AiVector.Zero, 5, in step, SoundKind.Footstep, -1, Shooter, new Tick(1), new AiRandom(1), out AiEvent e));
        Assert.Equal(SoundKind.Footstep, (SoundKind)e.Code);
        Assert.False(SoundTap.TryHear(new ActorId(4, 1), AiVector.Zero, 5, in step, SoundKind.Footstep, -1, Shooter, new Tick(1), new AiRandom(1), out _));
    }

    [Fact]
    public void TheWeaponTableNamesEachWeaponsReloadAndDryClick()
    {
        WeaponCatalog w = WeaponCatalog.Stock();
        Assert.Equal((SoundKind.Reload, w.IndexOf("weapon_smg1")), Classifier.Classify("Weapon_SMG1.Reload"));
        Assert.Equal((SoundKind.Reload, w.IndexOf("weapon_shotgun")), Classifier.Classify("Weapon_Shotgun.Reload"));
        Assert.Equal((SoundKind.DryFire, w.IndexOf("weapon_pistol")), Classifier.Classify("Weapon_Pistol.Empty"));
        Assert.Equal((SoundKind.Gunfire, w.IndexOf("weapon_357")), Classifier.Classify("Weapon_357.Single"));
    }

    [Fact]
    public void TheHeardPlaceIsNearerTheTruthWithAlertness()
    {
        float worst0 = 0f, worst10 = 0f;
        for (int i = 0; i < 50; i++)
        {
            var rng = new AiRandom((ulong)i + 1);
            var rng2 = new AiRandom((ulong)i + 1);
            var sound = new EmittedSound(new Vector(1000, 0, 0), 140, "Weapon_SMG1.Single", 2, [3]);
            SoundTap.TryHear(Bot, AiVector.Zero, 0, in sound, SoundKind.Gunfire, 0, Shooter, new Tick(1), rng, out AiEvent low);
            SoundTap.TryHear(Bot, AiVector.Zero, 10, in sound, SoundKind.Gunfire, 0, Shooter, new Tick(1), rng2, out AiEvent high);
            worst0 = MathF.Max(worst0, low.Position.DistanceTo(new AiVector(1000, 0, 0)));
            worst10 = MathF.Max(worst10, high.Position.DistanceTo(new AiVector(1000, 0, 0)));
        }

        Assert.InRange(worst10, 0.1f, 50f);
        Assert.InRange(worst0, 100f, 300f);
    }

    [Fact]
    public void AQuietSoundBeyondTheBotsShareOfItsReachIsNotHeard()
    {
        // A 75 dB step reaches 2000 / (20 / 25) = 2,500 u; Alertness 0 hears half of it.
        var sound = new EmittedSound(new Vector(2000, 0, 0), 75, "Concrete.StepLeft", 2, [3]);
        Assert.False(SoundTap.TryHear(Bot, AiVector.Zero, 0, in sound, SoundKind.Footstep, -1, Shooter, new Tick(1), new AiRandom(1), out _));
        Assert.True(SoundTap.TryHear(Bot, AiVector.Zero, 10, in sound, SoundKind.Footstep, -1, Shooter, new Tick(1), new AiRandom(1), out _));
    }
}
