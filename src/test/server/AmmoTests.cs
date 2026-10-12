using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The ammo table is a wire protocol, not just a list.
///
/// A weapon's ammo type is networked as its index into this table, so the client
/// only makes sense of it by having built the identical table. That is why the
/// list lives in the shared assembly, and it is why the properties below are
/// worth asserting: a duplicate name means two indices claim one type, and an
/// empty one means every weapon resolves its ammo to -1 — both of which are
/// silent, because the game keeps running and simply never gives anyone ammo.
/// </summary>
public class AmmoTests
{
    private static string[] Names() => AmmoTypes.All.Select(a => a.Name).ToArray();

    [Fact]
    public void TheAmmoTableIsNotEmpty()
        => Assert.True(AmmoTypes.All.Length > 0, "ammo table is not empty");

    [Fact]
    public void TheAmmoIndexIsTheWireFormat()
    {
        // plan_shared.md Phase 3. game/shared/ammodef.cpp is out of both
        // builds and this table is the authority, so this is the fact that
        // says what "the index is the wire format" costs: THE ORDER IS PINNED.
        //
        // A name inserted anywhere but the end renumbers every type after it.
        // The server then networks an index the client resolves to a different
        // ammo type -- a pistol reserve that fills from buckshot, and no error
        // anywhere, because both sides are doing exactly what they were told.
        // XBowBolt is at the END and its own comment says why: it was APPENDED
        // when the crossbow's reload was found reading zero forever.
        //
        // Appending to this list is fine and requires editing this line.
        // Inserting into it is a protocol break and requires an argument.
        Assert.Equal(
            new[] { "Pistol", "SMG1", "357", "Buckshot", "AR2",
                    "Grenade", "SMG1_Grenade", "AR2AltFire", "RPG_Round", "XBowBolt" },
            Names());
    }

    [SharedSourceFact("ammodef.h")]
    public void TheNativeGettersMovedIntoTheHeaderAndDidNotChange()
    {
        string text = File.ReadAllText(AnimListSources.Find("ammodef.h")!);

        // The .cpp is gone, so if the bodies are not here they are nowhere and
        // eleven translation units stop linking -- loud. What this catches is
        // the two clauses that could be quietly rewritten while still
        // compiling, and both are behaviour a test elsewhere would not see:
        //
        //   the search starts at 1, because index 0 is the invalid slot; a
        //   loop from 0 matches the zeroed name of an unregistered type;
        //
        //   an unknown index answers the DEFAULT splash size 4/8 and not 0,
        //   because zero is a bullet that makes no splash at all, which is a
        //   different bug from one that makes the default splash.
        Assert.Contains("for ( int i = 1; i < m_nAmmoIndex; i++ )", text);
        Assert.Contains("return 4;", text);
        Assert.Contains("return 8;", text);

        // And the constructor's index, which is the same fact as the loop's
        // lower bound seen from the other side.
        Assert.Contains("m_nAmmoIndex = 1;", text);
    }

    [Fact]
    public void AmmoNamesAreUnique()
    {
        var names = Names();

        Assert.True(names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length, "ammo names are unique");
    }

    [Fact]
    public void NoAmmoTypeIsUnnamed()
        => Assert.True(Names().All(n => !string.IsNullOrWhiteSpace(n)), "no ammo type is unnamed");

    [Fact]
    public void EveryAmmoTypeCanActuallyBeCarried()
    {
        // A carry limit of zero is indistinguishable at the call site from "not
        // registered": GiveAmmo caps at the limit and hands over nothing.
        Assert.True(AmmoTypes.All.All(a => a.MaxCarry > 0), "every ammo type can actually be carried");
    }

    [Fact]
    public void EveryStartingAmmoNameIsARegisteredType()
    {
        // Every weapon in the loadout is given ammo by name, and a name that is
        // not in the table gives silently nothing.
        var names = Names();

        var missing = SourceSharp.Server.GameRules.StartingAmmo
            .Select(pair => pair.Ammo)
            .Where(ammo => !names.Contains(ammo, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        foreach (string ammo in missing)
            Assert.Fail($"starting ammo '{ammo}' is a registered type");

        Assert.True(missing.Length == 0, "every starting ammo name is a registered type");
    }

    [Fact]
    public void AmmoTypesAreDeclaredWithADamageType()
    {
        // The scripts the weapon shell reads name their ammo, and the shell's
        // Precache resolves it through this same table on BOTH sides. A type a
        // script names but the table lacks is what "using undefined primary ammo
        // type" reports at startup.
        Assert.True(AmmoTypes.All.All(a => a.DamageType != 0), "ammo types are declared with a damage type");
    }
}
