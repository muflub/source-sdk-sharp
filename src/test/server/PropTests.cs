using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Physics props, and specifically the shape of breaking.
///
/// These are structural rather than behavioural because breaking ends in an
/// explosion and a removal, and both are engine calls -- there is nothing to
/// observe without a game. What CAN be checked without one is the thing that
/// actually went wrong: every explosive barrel in dm_lockdown is a
/// prop_physics_respawnable, that class overrode Break to hide itself instead
/// of removing itself, and the explosion lived in the half it replaced. The
/// barrels broke and never banged.
///
/// The fix was to stop letting a subclass replace breaking at all, so the test
/// is that breaking cannot be replaced.
/// </summary>
public class PropTests
{
    private const BindingFlags Any =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static MethodInfo? BreakOn(Type type)
        => type.GetMethod("Break", Any);

    [Fact]
    public void APhysicsPropHasABreak()
        => Assert.True(BreakOn(typeof(PropPhysics)) is not null, "a physics prop has a Break");

    [Fact]
    public void BreakingIsNotVirtual()
    {
        // The whole fix, in one assertion: a virtual Break is what allowed the
        // explosion to be dropped by overriding it.
        Assert.True(BreakOn(typeof(PropPhysics)) is { IsVirtual: false }, "breaking is not virtual, so no subclass can skip the explosion");
    }

    [Fact]
    public void WhatBecomesOfABrokenPropIsTheExtensionPoint()
        => Assert.True(typeof(PropPhysics).GetMethod("Broken", Any) is { IsVirtual: true }, "what becomes of a broken prop is the extension point");

    //-------------------------------------------------------------------------
    // Belt and braces: even a non-virtual Break can be hidden with "new".
    // Nothing does today, and this is what says so if something starts.
    //-------------------------------------------------------------------------

    private static Type[] Subclasses()
        => typeof(PropPhysics).Assembly.GetTypes()
               .Where(t => t != typeof(PropPhysics) && typeof(PropPhysics).IsAssignableFrom(t))
               .ToArray();

    [Fact]
    public void ThereArePhysicsPropSubclassesToCheck()
        => Assert.True(Subclasses().Length > 0, "there are physics prop subclasses to check");

    [Fact]
    public void NoSubclassDeclaresItsOwnBreak()
    {
        // NAMED rather than counted: which subclass grew a Break is the whole
        // answer, and the bulk fact this came out of would have stopped at the
        // first one anyway.
        var declared = Subclasses()
            .Where(t => t.GetMethod("Break", Any | BindingFlags.DeclaredOnly) is not null)
            .Select(t => t.Name)
            .ToArray();

        foreach (string name in declared)
            Assert.Fail($"{name} does not declare its own Break");

        Assert.True(declared.Length == 0, "no physics prop subclass declares its own Break");
    }

    //-------------------------------------------------------------------------
    // The respawnable one is the class the bug was in, so its own shape is
    // worth stating: it comes back, and coming back is the only thing it
    // changes about breaking.
    //-------------------------------------------------------------------------

    [Fact]
    public void ARespawnablePropOverridesOnlyWhatBecomesOfIt()
        => Assert.True(typeof(PropPhysicsRespawnable).GetMethod("Broken",
                BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly) is not null, "a respawnable prop overrides only what becomes of it");

    [Fact]
    public void ARespawnablePropIsRegisteredUnderItsMapName()
        => Assert.True(typeof(PropPhysicsRespawnable).GetCustomAttributes<ScriptedEntityAttribute>()
                .Any(a => a.ClassName == EntityNames.PropPhysicsRespawnable), "a respawnable prop is registered under its map name");
}
