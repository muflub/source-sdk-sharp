using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>game/server/client.cpp</c>'s <c>ClientPrecache()</c> — twenty-three asset
/// names — is <see cref="RopeAndBeamMaterials"/>,
/// <see cref="PlayerFeedbackSounds"/>, <see cref="WeaponSelectionSounds"/> and
/// <see cref="LegacyTempEntBounceSounds"/>.
///
/// <para>
/// EVERY NAME IS ASSERTED INDIVIDUALLY, and the totals are asserted beside them.
/// A count alone passes a rename; a spot check alone passes a deletion. Both are
/// needed because the failure mode of getting this wrong is not a build error
/// but a missing material or a silent sound at runtime, and the two totals are
/// the only thing that would catch a merge that dropped a row.
/// </para>
/// </summary>
public class ClientAssetsTests
{
    private static Assembly[] Server => [typeof(ServerModule).Assembly];

    private static HashSet<(PrecacheKind, string)> Declared() =>
        PrecacheRegistry.Declared(new RecordingLog(), Server).Select(d => (d.Kind, d.Asset)).ToHashSet();

    /// <summary>The seven, verbatim from <c>client.cpp:456-462</c>.</summary>
    public static readonly string[] StockMaterials =
    [
        "cable/cable.vmt",
        "cable/cable_lit.vmt",
        "cable/chain.vmt",
        "cable/rope.vmt",
        "sprites/blueglow1.vmt",
        "sprites/purpleglow1.vmt",
        "sprites/purplelaser1.vmt",
    ];

    /// <summary>The sixteen, verbatim from <c>client.cpp:465-487</c>.</summary>
    public static readonly string[] StockSounds =
    [
        "Hud.Hint",
        "Player.FallDamage",
        "Player.Swim",
        "Player.PickupWeapon",
        "Player.DenyWeaponSelection",
        "Player.WeaponSelected",
        "Player.WeaponSelectionClose",
        "Player.WeaponSelectionMoveSlot",
        "Bounce.Glass",
        "Bounce.Metal",
        "Bounce.Flesh",
        "Bounce.Wood",
        "Bounce.Shrapnel",
        "Bounce.ShotgunShell",
        "Bounce.Shell",
        "Bounce.Concrete",
    ];

    [Fact]
    public void EveryMaterialStockClientPrecacheAskedForIsStillDeclared()
    {
        var declared = Declared();

        foreach (string asset in StockMaterials)
        {
            Assert.True(declared.Contains((PrecacheKind.Model, asset)),
                $"ClientPrecache() precached '{asset}' with Precache_Model and nothing "
              + "declares it now. A rope draws as the error checkerboard and a beam draws "
              + "as nothing, on the client only, with no server-side symptom at all.");
        }
    }

    [Fact]
    public void EverySoundStockClientPrecacheAskedForIsStillDeclared()
    {
        var declared = Declared();

        foreach (string asset in StockSounds)
        {
            Assert.True(declared.Contains((PrecacheKind.Sound, asset)),
                $"ClientPrecache() precached '{asset}' with GameSound_PrecacheScript and "
              + "nothing declares it now. The engine warns once and plays silence — the "
              + "shape that hid DoSpark for months.");
        }
    }

    [Fact]
    public void TheFourClassesDeclareExactlySevenMaterialsBetweenThem()
    {
        // The TOTAL over the four classes, not over the assembly: a count over
        // the whole assembly would rise whenever any unrelated class gained a
        // [Precache] and stop meaning anything about this list.
        Type[] mine =
        [
            typeof(RopeAndBeamMaterials), typeof(PlayerFeedbackSounds),
            typeof(WeaponSelectionSounds), typeof(LegacyTempEntBounceSounds),
        ];

        int materials = mine
            .SelectMany(t => t.GetCustomAttributes<PrecacheAttribute>(inherit: false))
            .Count(a => a.Kind == PrecacheKind.Model);

        Assert.Equal(7, materials);
    }

    [Fact]
    public void TheFourClassesDeclareExactlySixteenSoundsBetweenThem()
    {
        Type[] mine =
        [
            typeof(RopeAndBeamMaterials), typeof(PlayerFeedbackSounds),
            typeof(WeaponSelectionSounds), typeof(LegacyTempEntBounceSounds),
        ];

        int sounds = mine
            .SelectMany(t => t.GetCustomAttributes<PrecacheAttribute>(inherit: false))
            .Count(a => a.Kind == PrecacheKind.Sound);

        Assert.Equal(16, sounds);
    }

    [Fact]
    public void NoneOfTheSixteenSoundsCarriesASlash()
    {
        // THE EQUIVALENCE THAT MAKES THIS A RELOCATION RATHER THAN A CHANGE.
        //
        // Stock called GameSound_PrecacheScript for all sixteen — a SOUNDSCRIPT
        // entry, not a wave path. SourceSharp_PrecacheSound picks between the two
        // by NAME SHAPE: no slash means a script name. So a name that grew a
        // slash would silently be precached as a raw wave that does not exist,
        // and the sound it names would be missing while the build stayed green.
        foreach (string asset in StockSounds)
        {
            Assert.True(!asset.Contains('/') && !asset.Contains('\\'),
                $"'{asset}' now contains a path separator, so SourceSharp_PrecacheSound "
              + "would send it to GameSound_Precache as a wave path instead of to "
              + "GameSound_PrecacheScript as stock did.");
        }
    }

    [Fact]
    public void TheGroupsKeepTheirOwnEntriesRatherThanBeingOneFlatList()
    {
        // The grouping is the only thing that says WHY each name is in the list,
        // and a precache has no other documentation: the failure is at runtime.
        Assert.Equal(7, typeof(RopeAndBeamMaterials)
            .GetCustomAttributes<PrecacheAttribute>(inherit: false).Count());
        Assert.Equal(3, typeof(PlayerFeedbackSounds)
            .GetCustomAttributes<PrecacheAttribute>(inherit: false).Count());
        Assert.Equal(5, typeof(WeaponSelectionSounds)
            .GetCustomAttributes<PrecacheAttribute>(inherit: false).Count());
        Assert.Equal(8, typeof(LegacyTempEntBounceSounds)
            .GetCustomAttributes<PrecacheAttribute>(inherit: false).Count());
    }

    [Fact]
    public void TheHudHintArmStockGuardedBehindHl2mpIsDeclaredBecauseThisBuildTookIt()
    {
        // Stock has `#ifndef HL2MP` around Hud.Hint. HL2MP is defined by no vpc
        // in this tree, so this build precached it — declaring it is preserving
        // behaviour, and NOT declaring it would have been the quiet kind of loss.
        Assert.Contains((PrecacheKind.Sound, "Hud.Hint"), Declared());
    }

    [Fact]
    public void TheSweepThatDrivesThemFindsThemThroughTheAssemblyScan()
    {
        // PrecacheRegistry.Scannable() is what ServerModule actually uses, and a
        // class declared in an assembly that scan does not reach is precached by
        // nothing. Asserting through Declared(Server) only would pass for a class
        // the live sweep never sees.
        var scanned = PrecacheRegistry.Declared(new RecordingLog(), PrecacheRegistry.Scannable())
            .Select(d => (d.Kind, d.Asset))
            .ToHashSet();

        foreach (string asset in StockMaterials)
            Assert.Contains((PrecacheKind.Model, asset), scanned);
        foreach (string asset in StockSounds)
            Assert.Contains((PrecacheKind.Sound, asset), scanned);
    }
}
