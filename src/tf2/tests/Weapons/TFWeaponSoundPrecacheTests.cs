using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The sounds a TF weapon plays are precached before it plays them: every
/// entry its script's <c>SoundData</c> names, stock's whole
/// <c>CBaseCombatWeapon::Precache</c> loop over <c>aShootSounds</c>
/// (<c>shared/basecombatweapon_shared.cpp:303-308</c>) — not only the named
/// fields the row carries for playing. The rocket launcher's burst crit
/// (<c>Weapon_RPG.SingleCrit</c>) is the one beyond them: a crit shot plays it
/// and an unprecached crit is a console error and a silent shot.
/// </summary>
public sealed class TFWeaponSoundPrecacheTests
{
    private static string[] Precached()
    {
        var engine = TestServices.Mock();
        engine.Strict = false;

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        var arsenal = new Arsenal(new RecordingLog(), engine, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine, new DamageHitGroups(),
                                  weapons: new TFWeaponWorld().Catalog);

        arsenal.PrecacheAll();

        return engine.Calls.Where(c => c.Member == "Precache.PrecacheSound")
                           .Select(c => (string)c.Args[0]!)
                           .ToArray();
    }

    [Fact]
    public void The_rows_named_sounds_are_precached()
    {
        Assert.Contains("Weapon_RPG.Single", Precached());
    }

    /// <summary>The control is the fields themselves: the precache ran, and reached
    /// the rocket launcher's row. The fact after this one is the burst crit.</summary>
    [Fact]
    public void A_script_sound_beyond_the_named_fields_is_precached()
    {
        Assert.Contains("Weapon_RPG.SingleCrit", Precached());
    }
}
