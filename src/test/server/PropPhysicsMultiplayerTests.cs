using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// prop_physics_multiplayer's one idea: which of stock's four physicsmodes a
/// map asked for, and what this hierarchy can actually serve.
///
/// <para>
/// The arithmetic is a static function of two numbers deliberately, so these
/// tests need neither a map nor a solver — and so that the one rule that is
/// NOT stock's (clientside served on the server) is stated somewhere that goes
/// red if someone quietly restores stock's UTIL_Remove.
/// </para>
/// </summary>
public class PropPhysicsMultiplayerTests
{
    /// <summary>The spawnflag stock calls SF_PHYSPROP_FORCE_SERVER_SIDE.</summary>
    private const int ForceServerSide = 1 << 14;

    [Fact]
    public void AMapThatSetsNoPhysicsmodeAsksForAutodetect()
    {
        Assert.True(PropPhysicsMultiplayer.ResolveMode(0, 0) == PhysicsMode.Autodetect,
                    "a prop_physics_multiplayer with no physicsmode key is autodetect");
    }

    [Fact]
    public void TheTwoServerSideModesArePassedThroughUnchanged()
    {
        Assert.True(PropPhysicsMultiplayer.ResolveMode(1, 0) == PhysicsMode.Solid,
                    "physicsmode 1 is solid and simulated on the server");
        Assert.True(PropPhysicsMultiplayer.ResolveMode(2, 0) == PhysicsMode.NonSolid,
                    "physicsmode 2 is simulated on the server and collides with nothing");
    }

    /// <summary>
    /// The rule that is this build's rather than stock's. Stock removes a
    /// clientside prop from the server and lets the client simulate it; there
    /// is no client-side prop simulation on this hierarchy, so removing it
    /// would take the prop out of the map altogether.
    /// </summary>
    [Fact]
    public void AClientsidePropIsServedOnTheServerRatherThanVanishing()
    {
        Assert.True(PropPhysicsMultiplayer.ResolveMode(3, 0) == PhysicsMode.NonSolid,
                    "physicsmode 3 is served on the server here, not removed");
    }

    [Fact]
    public void TheForceServerSideSpawnflagBeatsEveryPhysicsmode()
    {
        foreach (int key in new[] { 0, 1, 2, 3 })
        {
            Assert.True(PropPhysicsMultiplayer.ResolveMode(key, ForceServerSide) == PhysicsMode.NonSolid,
                        $"the force-server-side spawnflag beats physicsmode {key}");
        }
    }

    /// <summary>
    /// A physicsmode a map should never write. Stock's switch has no default
    /// either and leaves the member at its constructed AUTODETECT, which is the
    /// behaviour a garbled key has always had.
    /// </summary>
    [Fact]
    public void AnUnknownPhysicsmodeFallsBackToAutodetectRatherThanToAMode()
    {
        Assert.True(PropPhysicsMultiplayer.ResolveMode(97, 0) == PhysicsMode.Autodetect,
                    "a physicsmode outside stock's four is autodetect, not a mode");
    }

    /// <summary>
    /// The numbers belong to maps, not to this file. A renumbering of the enum
    /// would silently turn every physicsmode 1 in every map into something
    /// else, and nothing else in the build would notice.
    /// </summary>
    [Fact]
    public void ThePhysicsmodeNumbersAreStocksAndAreNotThisFilesToChange()
    {
        Assert.True((int)PhysicsMode.Autodetect == 0, "PHYSICS_MULTIPLAYER_AUTODETECT is 0");
        Assert.True((int)PhysicsMode.Solid == 1, "PHYSICS_MULTIPLAYER_SOLID is 1");
        Assert.True((int)PhysicsMode.NonSolid == 2, "PHYSICS_MULTIPLAYER_NON_SOLID is 2");
        Assert.True((int)PhysicsMode.ClientSide == 3, "PHYSICS_MULTIPLAYER_CLIENTSIDE is 3");
    }
}
