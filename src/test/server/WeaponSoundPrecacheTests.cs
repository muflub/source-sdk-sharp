using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The sounds a weapon plays are precached before it plays them: every sound
/// its script's SoundData names (CBaseCombatWeapon::Precache), and the
/// crossbow bolt's own (CCrossbowBolt::Precache).
///
/// <para>
/// Found side by side with stock (tools/stock-ab --weapons weapon_crossbow,
/// 2026-10-25): this mod's server logged "SV_StartSound: ... not precached"
/// for the bolt's flight, hit and load sounds, and so never played them.
/// </para>
/// </summary>
public class WeaponSoundPrecacheTests
{
    private const string CrossbowScript = """
        WeaponData
        {
            "SoundData"
            {
                "single_shot" "Weapon_Crossbow.Single"
                "reload"      "Weapon_Crossbow.Reload"
                "special1"    "Weapon_Crossbow.BoltElectrify"
                "special2"    "Weapon_Crossbow.BoltFly"
            }
        }
        """;

    private static RecordingEngine Precached(WeaponScriptDatabase? scripts)
    {
        var engine = Mock();
        engine.Strict = false;
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        var arsenal = new Arsenal(new RecordingLog(), engine, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine, new DamageHitGroups(), scripts: scripts);

        arsenal.PrecacheAll();
        return engine;
    }

    private static string[] SoundsPrecached(RecordingEngine engine)
        => engine.Calls.Where(c => c.Member == "Precache.PrecacheSound").Select(c => (string)c.Args[0]!).ToArray();

    [Fact]
    public void EverySoundAWeaponsScriptNamesIsPrecached()
    {
        var scripts = new WeaponScriptDatabase(new RecordingLog(),
                                               path => path == "scripts/weapon_crossbow.txt" ? CrossbowScript : null);

        string[] sounds = SoundsPrecached(Precached(scripts));

        Assert.Contains("Weapon_Crossbow.BoltFly", sounds);
        Assert.Contains("Weapon_Crossbow.BoltElectrify", sounds);
    }

    /// <summary>The control: without the script, the special sounds are nowhere in the precache.</summary>
    [Fact]
    public void WithNoScriptOnlyTheRowsSoundsArePrecached()
    {
        string[] sounds = SoundsPrecached(Precached(scripts: null));

        Assert.Contains("Weapon_Crossbow.Single", sounds);
        Assert.DoesNotContain("Weapon_Crossbow.BoltFly", sounds);
    }

    [Fact]
    public void TheBoltPrecachesTheSoundsItsTouchPlays()
    {
        var declared = PrecacheRegistry.Declared(new RecordingLog(), [typeof(Bolt).Assembly])
                                       .Where(d => d.Kind == PrecacheKind.Sound)
                                       .Select(d => d.Asset).ToHashSet();

        Assert.Contains(Bolt.HitBodySound, declared);
        Assert.Contains(Bolt.HitWorldSound, declared);
    }
}
