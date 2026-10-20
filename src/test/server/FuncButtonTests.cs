using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>func_button</c>'s damage path, and the output that used to be dead.
///
/// <para>
/// The output must be BOUND for any of this to mean anything.
/// <c>Output.Fire</c> returns without counting when it has no owner, so a button
/// built with a bare <c>new FuncButton()</c> reports <c>FireCount == 0</c>
/// whatever the entity does. The test this file replaced did exactly that, which
/// means it could not have gone red even once the bug was fixed — it was pinning
/// an unbound output, not a dead one. Every test here spawns through the
/// registry, the way the game does.
/// </para>
/// </summary>
public class FuncButtonTests
{
    /// <summary>SF_BUTTON_DAMAGE_ACTIVATES. Stock's bit, named here so the tests read.</summary>
    private const int DamageActivates = 512;

    /// <summary>
    /// A live button with its outputs bound, its spawnflags whatever the test
    /// says, and nothing else pretended.
    /// </summary>
    private static FuncButton Spawn(int spawnFlags, params (string Key, string Value)[] keys)
    {
        var services = Services(out var engine);
        engine.Returns("Entities.GetSpawnFlags", spawnFlags);

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncButton).Assembly);
        registry.Services = services;

        var handle = new EntityHandle(1);

        foreach (var (key, value) in keys)
            registry.KeyValue(handle, EntityNames.FuncButton, key, value);

        registry.Spawn(handle, EntityNames.FuncButton);
        return (FuncButton)registry.Get(handle)!;
    }

    /// <summary>
    /// The map-facing name is the whole point of the rename. The C# field is
    /// <c>Damaged</c> — it had to move, because <c>OnDamaged</c> is the base's
    /// damage virtual and a field cannot share a name with a method — but what a
    /// mapper connects, and what stock's FGD lists, is still "OnDamaged".
    /// </summary>
    [Fact]
    public void TheOutputStillBindsUnderTheNameAMapWrites()
    {
        var button = Spawn(spawnFlags: 0);

        Assert.True(button.Damaged.Name == "OnDamaged",
            "the renamed field still binds under the name a map wires");

        Assert.True(button.OnPressed.Name == "OnPressed", "and the outputs around it are unchanged");
    }

    /// <summary>
    /// Stock's first line in <c>CBaseButton::OnTakeDamage</c> fires
    /// <c>m_OnDamaged</c> before it asks anything else, so a hit that does
    /// nothing to the button is still reported. This is the assertion the old
    /// pinned test held the opposite of.
    /// </summary>
    [Fact]
    public void DamagingAButtonFiresItsOnDamagedOutput()
    {
        var button = Spawn(spawnFlags: 0);

        Assert.True(button.Damaged.FireCount == 0, "a fresh button has fired nothing");

        // The route the engine takes: OnDamagedAt is what native calls, and its
        // default hands to OnDamaged, which this class now overrides.
        int taken = button.OnDamagedAt(EntityHandle.None, 50f, DamageType.Bullet, default, default);

        Assert.True(button.Damaged.FireCount == 1, "shooting the button fires its OnDamaged output");

        Assert.True(taken == 0, "and the button takes no damage, as every path in stock's returns zero");

        Assert.True(button.OnPressed.FireCount == 0,
            "without the damage-activates flag, being shot reports the hit and does not press it");
    }

    /// <summary>
    /// The spawnflag is how the button is pressed at all in the maps that use
    /// it: shooting it IS the press. Firing the output without activating would
    /// look right in a log and leave the map dead.
    /// </summary>
    [Fact]
    public void DamageActivatesPressesTheButton()
    {
        var button = Spawn(DamageActivates);

        button.OnDamagedAt(EntityHandle.None, 50f, DamageType.Bullet, default, default);

        Assert.True(button.Damaged.FireCount == 1, "the hit is reported");
        Assert.True(button.OnPressed.FireCount == 1, "and with the flag set, the hit presses the button");
        Assert.True(button.OnIn.FireCount == 1, "which takes it in, as a press from any other source would");
    }

    /// <summary>
    /// A pressed button is locked out until its wait elapses, and stock's damage
    /// path treats that as a silent return — no lock sound, no
    /// <c>OnUseLocked</c>, unlike the USE path, which reports it. The output
    /// still fires, because stock fires it before it looks at the lock.
    /// </summary>
    [Fact]
    public void ASecondHitWhileLockedOutReportsTheHitAndPressesNothing()
    {
        var button = Spawn(DamageActivates);

        button.OnDamagedAt(EntityHandle.None, 50f, DamageType.Bullet, default, default);
        button.OnDamagedAt(EntityHandle.None, 50f, DamageType.Bullet, default, default);

        Assert.True(button.Damaged.FireCount == 2, "both hits are reported");
        Assert.True(button.OnPressed.FireCount == 1, "the second presses nothing, because the button is still in");

        Assert.True(button.OnUseLocked.FireCount == 0,
            "and the damage path stays silent about the lock, where stock's use path reports it");
    }

    /// <summary>
    /// A button the map starts disabled is locked from spawn. The same rule
    /// reached a different way, and worth its own test because it is the case a
    /// mapper hits when the button is meant to be armed by something else first.
    /// </summary>
    [Fact]
    public void ADisabledButtonReportsTheHitAndStaysShut()
    {
        var button = Spawn(DamageActivates, ("startdisabled", "1"));

        button.OnDamagedAt(EntityHandle.None, 50f, DamageType.Bullet, default, default);

        Assert.True(button.Damaged.FireCount == 1, "a locked button still reports being hit");
        Assert.True(button.OnPressed.FireCount == 0, "and does not press");
    }

    /// <summary>
    /// Every output a map may wire is declared and bound under the name stock's
    /// FGD lists, so a connection to any of them resolves.
    /// </summary>
    [Fact]
    public void TheOutputsAreDeclaredSoAMapCanWireThem()
    {
        var button = Spawn(spawnFlags: 0);

        Assert.True(button.Damaged.Name == "OnDamaged", "the button declares an OnDamaged output");
        Assert.True(button.OnPressed.Name == "OnPressed", "and the OnPressed one that does work");
        Assert.True(button.OnUseLocked.Name == "OnUseLocked", "and the one a locked use reports through");
        Assert.True(button.OnIn.Name == "OnIn" && button.OnOut.Name == "OnOut", "and the pair that bracket a press");
    }
}
