using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The player-adjacent point entities: info_intermission, player_weaponstrip and
/// player_speedmod.
///
/// What each of them owns is small and quiet when it is wrong. A sign error in
/// the intermission camera's pitch aims it at the sky, which nobody sees until
/// somebody wins the round; and a speedmod that translates one spawnflag to the
/// wrong button bit leaves the player able to sprint out of a scripted moment,
/// which reads as the map being broken rather than as this.
/// </summary>
public class PlayerEntityTests
{
    private static readonly EntityHandle Ent = new(0x0031_0001u);

    /// <summary>
    /// That the strip entity answers to both of stock's input names.
    ///
    /// The two names are the whole interface, and the suit is the difference
    /// between them — a map fires them by string, so a handler bound under a name
    /// nobody writes registers, spawns and does nothing at all until somebody
    /// wonders why the suit survived.
    /// </summary>
    [Fact]
    public void StripInputs()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PlayerWeaponStrip).Assembly);
        registry.Services = Ignoring("Entities.FindByName", "Entities.IsPlayer", "Entities.IsValid");

        Assert.True(registry.IsRegistered(EntityNames.PlayerWeaponstrip), "player_weaponstrip is a managed class");

        registry.Spawn(Ent, EntityNames.PlayerWeaponstrip);

        // Taken, not carried out: there is no engine here, so the activator is
        // not a player and nothing is removed. What is asked is whether the name
        // reached a handler.
        Assert.True(registry.Input(Ent, "Strip", "", 0f, EntityHandle.None), "Strip reaches a handler");

        Assert.True(registry.Input(Ent, "StripWeaponsAndSuit", "", 0f, EntityHandle.None), "and so does StripWeaponsAndSuit");

        // Stock's name for the first input is "Strip", not "StripWeapons" —
        // the C++ method is InputStripWeapons and only the datadesc says which
        // string a map writes.
        Assert.True(!registry.Input(Ent, "StripWeapons", "", 0f, EntityHandle.None), "StripWeapons is not one of them");

        registry.Clear();
    }

    /// <summary>
    /// The spawnflag-to-button-bit table, which is the only arithmetic in
    /// player_speedmod and the only part of it reachable without an engine.
    ///
    /// Worth checking bit by bit rather than in aggregate: this is a translation
    /// between two unrelated numbering schemes, and the enum it translates INTO
    /// was wrong about three of its own members until this entity needed them —
    /// IN_SCORE was missing from the list, so everything above it was named one
    /// bit too low.
    /// </summary>
    [Fact]
    public void SpeedModButtons()
    {
        Assert.True(PlayerSpeedMod.DisabledButtons(PlayerSpeedMod.SpawnFlag.None) == 0, "no flags takes nothing away");

        Assert.True(PlayerSpeedMod.DisabledButtons(PlayerSpeedMod.SpawnFlag.SuppressJump) == InputButtons.Jump, "the jump flag takes the jump key");

        Assert.True(PlayerSpeedMod.DisabledButtons(PlayerSpeedMod.SpawnFlag.SuppressDuck) == InputButtons.Duck, "the duck flag takes the duck key");

        Assert.True(PlayerSpeedMod.DisabledButtons(PlayerSpeedMod.SpawnFlag.SuppressUse) == InputButtons.Use, "the use flag takes the use key");

        // IN_SPEED, which is 1<<17 and not 1<<16 — the scoreboard is 1<<16 and
        // never reaches the server, so a version that ran them together would
        // disable nothing and look like a flag the mapper had not set.
        Assert.True(PlayerSpeedMod.DisabledButtons(PlayerSpeedMod.SpawnFlag.SuppressSpeed) == InputButtons.Speed
            && (int)InputButtons.Speed == 1 << 17, "the speed flag takes the sprint key, at bit 17");

        // One flag, both triggers. Stock says so in a single line, and a version
        // that took only the primary would leave the alt-fire working.
        Assert.True(PlayerSpeedMod.DisabledButtons(PlayerSpeedMod.SpawnFlag.SuppressAttack)
                == (InputButtons.Attack | InputButtons.Attack2), "the attack flag takes both triggers");

        Assert.True(PlayerSpeedMod.DisabledButtons(PlayerSpeedMod.SpawnFlag.SuppressZoom) == InputButtons.Zoom
            && (int)InputButtons.Zoom == 1 << 19, "the zoom flag takes the zoom key, at bit 19");

        // The two flags that are not buttons at all. A table that let either of
        // them fall through would disable a key nobody asked about.
        Assert.True(PlayerSpeedMod.DisabledButtons(PlayerSpeedMod.SpawnFlag.SuppressWeapons | PlayerSpeedMod.SpawnFlag.SuppressHud) == 0, "the weapon and HUD flags take no keys");

        Assert.True(PlayerSpeedMod.DisabledButtons(PlayerSpeedMod.SpawnFlag.SuppressJump | PlayerSpeedMod.SpawnFlag.SuppressUse)
                == (InputButtons.Jump | InputButtons.Use), "flags combine rather than replace");
    }

    /// <summary>
    /// That player_speedmod answers to ModifySpeed and nothing else, and that the
    /// input survives having no player to act on.
    /// </summary>
    [Fact]
    public void SpeedModInputs()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PlayerSpeedMod).Assembly);
        registry.Services = Ignoring("Entities.FindByName", "Entities.IsPlayer", "Entities.IsValid");

        Assert.True(registry.IsRegistered(EntityNames.PlayerSpeedmod), "player_speedmod is a managed class");

        registry.Spawn(Ent, EntityNames.PlayerSpeedmod);

        Assert.True(registry.Input(Ent, "ModifySpeed", "0.2", 0.2f, EntityHandle.None), "ModifySpeed reaches a handler");

        // Both directions, because they are separate branches: one suppresses and
        // one restores, and an unguarded lookup in either would take the map down.
        Assert.True(registry.Input(Ent, "ModifySpeed", "1", 1f, EntityHandle.None), "and so does the value that gives it all back");

        Assert.True(!registry.Input(Ent, "SetSpeed", "1", 1f, EntityHandle.None), "player_speedmod claims nothing else");

        registry.Clear();
    }

    [Fact]
    public void Aim()
    {
        var here = new Vector(0f, 0f, 0f);

        // Due east along +X is Source's zero facing, so it must come back as
        // nothing rather than as 360 or as -0.
        var east = InfoIntermission.Facing(here, new Vector(128f, 0f, 0f));
        Assert.True(east.X == 0f && east.Y == 0f, "looking along +X is zero pitch and zero yaw");

        // +Y is a quarter turn anticlockwise, which is what a yaw of 90 means.
        var north = InfoIntermission.Facing(here, new Vector(0f, 128f, 0f));
        Assert.True(Near(north.Y, 90f) && Near(north.X, 0f), "looking along +Y yaws 90");

        // The sign that goes wrong. A target BELOW the camera is a POSITIVE
        // pitch in Source, and a version that read the sign the intuitive way
        // would aim the end-of-round camera at the ceiling.
        var below = InfoIntermission.Facing(new Vector(0f, 0f, 128f), new Vector(128f, 0f, 0f));
        Assert.True(Near(below.X, 45f) && Near(below.Y, 0f), "a target 45 degrees below pitches +45");

        var above = InfoIntermission.Facing(here, new Vector(128f, 0f, 128f));
        Assert.True(Near(above.X, 315f) && Near(above.Y, 0f), "a target 45 degrees above pitches -45");

        // Straight up and straight down have no yaw to compute, and atan2 of two
        // zeroes would pick one at random. Stated in the range the rest uses:
        // 270 rather than -90.
        var overhead = InfoIntermission.Facing(here, new Vector(0f, 0f, 128f));
        Assert.True(overhead.X == 270f && overhead.Y == 0f, "straight up is a defined pitch and no yaw");

        var underfoot = InfoIntermission.Facing(new Vector(0f, 0f, 128f), here);
        Assert.True(underfoot.X == 90f && underfoot.Y == 0f, "straight down is a defined pitch and no yaw");

        // Roll is not something a direction carries, and a camera given one
        // would show the map tilted.
        Assert.True(east.Z == 0f && below.Z == 0f && overhead.Z == 0f, "no roll is ever produced");
    }

    [Fact]
    public void Keys()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(InfoIntermission).Assembly);
        registry.Services = Ignoring("Entities.FindByName", "Entities.IsPlayer", "Entities.IsValid");

        Assert.True(registry.IsRegistered(EntityNames.InfoIntermission), "info_intermission is a managed class");

        // The only key it has. Claimed by the FIELD rather than falling through
        // to OnKeyValue, which is what says the binding is in place — an
        // unclaimed "target" would leave the camera aimed at nothing and report
        // nothing about it.
        bool claimed = registry.KeyValue(Ent, EntityNames.InfoIntermission, "target", "the_statue");
        Assert.True(claimed, "the target key binds to a field");

        registry.Spawn(Ent, EntityNames.InfoIntermission);
        Assert.True(registry.Get(Ent) is InfoIntermission, "spawning produces the managed instance");

        // Stock's Think gives up when the target is missing, and so does this.
        // Worth a check because it runs with no engine to ask here: an
        // unguarded lookup would take the whole map's spawn down.
        registry.Think(Ent);
        Assert.True(registry.Get(Ent) is InfoIntermission, "a think with no target to find survives it");
    }

    /// <summary>
    /// Degrees, compared loosely. The answers come out of two atan2 calls, so
    /// exact equality is only available for the axis-aligned cases above.
    /// </summary>
    private static bool Near(float a, float b) => MathF.Abs(a - b) < 0.01f;
}
