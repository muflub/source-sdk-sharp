using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Server;
using TF2.Server.Combat;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Sniper lane's measure of kills (<c>/ai/sniper/kills</c>): a kill counts for its scorer under the victim's class, and as a
/// headshot only when the killing hit was the scorer's and a headshot's kind.
/// </summary>
public sealed class TFSniperKillTallyTests
{
    private const int SniperIndex = 3, MedicIndex = 4, HeavyIndex = 5;
    private static readonly EntityHandle Sniper = new(0x0001_0000u | SniperIndex), Medic = new(0x0001_0000u | MedicIndex), Heavy = new(0x0001_0000u | HeavyIndex);

    private sealed class Owners : ITFWeaponOwners
    {
        public TFClass ClassOf(int playerIndex) => playerIndex switch { MedicIndex => TFClass.Medic, HeavyIndex => TFClass.HeavyWeapons, _ => TFClass.Sniper };
        public TFConditions? ConditionsOf(int playerIndex) => null;
        public float CritMult(int playerIndex) => 1f;
        public bool WinsCrits(int playerIndex) => false;
        public IAttributeProvider? ItemOf(int playerIndex, string weaponName) => null;
        public IAttributeProvider? ManagerOf(int playerIndex) => null;
        public bool OffGround(int playerIndex) => false;
        public bool EyesUnderwater(int playerIndex) => false;
        public bool DuckingOf(int playerIndex) => false;
    }

    private sealed class NoFields : IDeathNoticeFields
    {
        public void SetInt(string key, int value) { }
        public void SetBool(string key, bool value) { }
        public void SetString(string key, string value) { }
    }

    private static (TFSniperKillTally Tally, TFSniperKillCounts Counts, TFDamageRecords Records) Make()
    {
        var records = new TFDamageRecords();
        var counts = new TFSniperKillCounts();
        return (new TFSniperKillTally(records, new Owners(), counts), counts, records);
    }

    private static void Kill(TFSniperKillTally tally, TFDamageRecords records, EntityHandle victim, TFDamageCustom custom, EntityHandle? hitBy = null)
    {
        records.Note(victim.EntryIndex, new TFHitRecord(hitBy ?? Sniper, 150f, DamageType.Bullet, TFCritType.Full, custom, TFWeaponId.None, "", 1f), damager: true);
        tally.Fill(new DeathNoticeFacts(victim, Sniper, Sniper, Sniper, "sniperrifle"), new NoFields());
    }

    /// <summary>Two kills on a Medic and one on a Heavy count under each victim's class.</summary>
    [Fact]
    public void KillsCountByTheVictimsClass()
    {
        (TFSniperKillTally tally, TFSniperKillCounts counts, TFDamageRecords records) = Make();
        Kill(tally, records, Medic, TFDamageCustom.None);
        Kill(tally, records, Medic, TFDamageCustom.None);
        Kill(tally, records, Heavy, TFDamageCustom.None);
        Assert.Equal((2, 1, 0), (counts.Kills(SniperIndex, TFClass.Medic), counts.Kills(SniperIndex, TFClass.HeavyWeapons), counts.Kills(SniperIndex, TFClass.Scout)));
    }

    /// <summary>A kill whose last hit was the scorer's headshot is a headshot; a body shot's is not, nor a headshot by someone else.</summary>
    [Fact]
    public void AHeadshotIsTheScorersOwnHeadshot()
    {
        (TFSniperKillTally tally, TFSniperKillCounts counts, TFDamageRecords records) = Make();
        Kill(tally, records, Medic, TFDamageCustom.Headshot);
        Kill(tally, records, Heavy, TFDamageCustom.None);
        Kill(tally, records, Medic, TFDamageCustom.Headshot, hitBy: Heavy);
        Assert.Equal(1, counts.Headshots(SniperIndex));
    }
    /// <summary>A match reset (waiting for players ended) and a level's end clear the tally, as they clear the scoreboard.</summary>
    [Fact]
    public void AResetAndALevelsEndClearIt()
    {
        (TFSniperKillTally tally, TFSniperKillCounts counts, TFDamageRecords records) = Make();
        Kill(tally, records, Medic, TFDamageCustom.Headshot);
        int before = counts.Kills(SniperIndex, TFClass.Medic);
        counts.OnMatchReset("waiting for players");
        int afterReset = counts.Kills(SniperIndex, TFClass.Medic) + counts.Headshots(SniperIndex);
        Kill(tally, records, Heavy, TFDamageCustom.None);
        ((IGameLifecycle)counts).OnLevelShutdown();
        Assert.Equal((1, 0, 0), (before, afterReset, counts.Kills(SniperIndex, TFClass.HeavyWeapons)));
    }
    /// <summary>A soak counts once when it comes on, not again while it lasts; another counts when it comes on again.</summary>
    [Fact]
    public void ASoakCountsWhenItComesOn()
    {
        var counts = new TFSniperKillCounts();
        var soaks = new TFSniperSoakTally(null!, counts);
        bool on = false;
        foreach (bool frame in new[] { false, true, true, true, false, true })
        {
            on = frame;
            soaks.Observe(i => i == 4 && on);
        }

        Assert.Equal(2, counts.Soaked);
    }
}
