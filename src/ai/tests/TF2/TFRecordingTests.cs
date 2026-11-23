using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Ai.Tests.Record;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A TF2 recording replays the class play it recorded (format 10, review-p7f F1): the views carry the bot's class,
/// conditions and metal, the sighted actors' class, conditions, borrowed name and facing, and the bot's HUD (its kit,
/// meters and mates), so a decider that reads its kit decides in the replay as it did live.
/// </summary>
public sealed class TFRecordingTests
{
    private static readonly ActorId Scout = new(2, 1);
    private static readonly byte[] Levels = [4, 4, 4, 3, 3, 3, 4, 3, 4, 3];
    private const int Scattergun = 20, Pistol = 21, Bat = 22;

    private static readonly WeaponKnowledge Weapons = new([
        .. TFField.Weapons.All,
        new WeaponFacts(Scattergun, "tf_weapon_scattergun", 6, 0.625f, 3f, true, true, 0f),
        new WeaponFacts(Pistol, "tf_weapon_pistol_scout", 12, 0.15f, 1f, true, false, 0f),
        new WeaponFacts(Bat, "tf_weapon_bat", 0, 0.5f, 0f, false, false, 0f),
    ]);

    private static TFHud ScoutKit()
        => new([new TFKitItem(TFLoadoutSlot.Primary, 13, "tf_weapon_scattergun", Scattergun, []),
                new TFKitItem(TFLoadoutSlot.Secondary, 23, "tf_weapon_pistol", Pistol, []),
                new TFKitItem(TFLoadoutSlot.Melee, 0, "tf_weapon_bat", Bat, [new TFOwnKitAttribute("mult_dmg", 1.1f)])],
               [new TFMeter("scout.hype", 0.5f)], []);

    private static MemoryWorld World()
        => new(new MapKnowledge("tf_field", [], [], Weapons, [new TeamSpawn(TFField.Red, TFField.RedSpawn), new TeamSpawn(TFField.Blue, TFField.BlueSpawn)],
                                TFAbilities.Table()), null, 0.015f, id => id.ToString());

    private static IDecider Decider(BrainSetup setup) => new TFDecider(setup, TFKnowledge.Classes(c => (300f, 125))) { Feel = TFFeelSwitches.All };

    /// <summary>A Scout holding its bat with nobody in sight, its HUD's kit in each view, for 60 ticks, recorded.</summary>
    private static byte[] Live()
    {
        var bytes = new MemoryStream();
        var recorder = new AiRecorder(bytes, Recordings.Header);
        var session = new AiSession(new InlineTransport(), recorder, Recordings.Header.Slots, Recordings.Header.SightedCapacity)
        {
            World = World(), Curves = SourceSharp.Ai.Tests.Skills.Curves.Game(), Deciders = Decider,
        };
        session.AddBot(Scout, 5, Levels);
        TFHud kit = ScoutKit();
        for (int tick = 1; tick <= 60; tick++)
        {
            AiSnapshot snap = session.Snapshots.BeginWrite(new Tick(tick));
            PerceivedView v = snap.Add(Scout, TFField.Self(new AiVector(2048f, 256f, 0f), weapon: Bat, clip: 0, playerClass: TFClass.Scout));
            v.SetGameHud(kit);
            session.Publish(snap);
        }

        session.Drain(wait: true);
        return bytes.ToArray();
    }

    /// <summary>
    /// The Scout's lull takes out its own scattergun (its kit's primary) live, and the replay of that recording does the
    /// same, byte for byte. Without the HUD in the recording the replay's Scout had no kit and took nothing out.
    /// </summary>
    [Fact]
    public void AScoutWithItsBatAndNobodyInSightReplaysIdentically()
    {
        byte[] recording = Live();
        IntentCommand[] intents = [.. Recordings.Records(recording).Where(r => r.Type == AiRecordType.Intent)
                                                 .Select(r => AiRecordFormat.ReadIntent(new BinaryReader(new MemoryStream(r.Payload))))];
        Assert.Contains(intents, i => i.Action.Kind == ActionKind.SelectWeapon && i.Action.WeaponSlot == EngineBotSelf.ByKind + Scattergun);
        AiReplayResult r = AiReplayer.Replay(new MemoryStream(recording), new InlineTransport(), World(), SourceSharp.Ai.Tests.Skills.Curves.Game(), Decider, null);
        Assert.True(r.RecordedIntentCount > 0);
        Assert.True(r.Identical, $"{r.RecordedIntentCount} recorded, {r.ReplayedIntentCount} replayed");
    }

    /// <summary>The HUD's recorded bytes read back as the same kit, meters and mates.</summary>
    [Fact]
    public void AHudsRecordedBytesReadBackAsTheSameHud()
    {
        TFHud hud = new([.. ScoutKit().Kit], [.. ScoutKit().Meters], [new TFMateHealth(4, 100, 300, TFMateSeen.HealTarget, new AiVector(1f, 2f, 3f))]);
        TFHud back = TFHud.FromRecorded(hud.Recorded());
        Assert.Equal(hud.Kit.Select(k => (k.Slot, k.Defindex, k.ItemClass, k.WeaponKind, string.Join(",", k.Attributes))),
                     back.Kit.Select(k => (k.Slot, k.Defindex, k.ItemClass, k.WeaponKind, string.Join(",", k.Attributes))));
        Assert.Equal(hud.Meters.ToArray(), back.Meters.ToArray());
        Assert.Equal(hud.Mates.ToArray(), back.Mates.ToArray());
    }
}
