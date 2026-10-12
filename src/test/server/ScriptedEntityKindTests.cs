using System;
using System.Linq;
using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The tests that would have caught a dead entity kind.
///
/// <para>
/// <c>ScriptedEntityKind.Surface</c> was declared, mirrored into native as
/// <c>SOURCESHARP_SCRIPTED_SURFACE</c>, and given a <c>case</c> in the
/// registration switch whose own comment called it "the last kind to get here".
/// Nothing ever passed it. <c>func_breakable_surf</c> declares
/// <see cref="ScriptedEntityKind.Brush"/>, and the two branches installed
/// identically, so the kind cost nothing and announced nothing — a value in a
/// public enum that could be selected, documented at length, and had no
/// behaviour of its own to be wrong.
/// </para>
///
/// <para>
/// An enum whose values are ABI numbers cannot be checked by the compiler:
/// every value is reachable from C# by construction, and the native side takes
/// an <c>int</c>. What CAN be checked is the thing that actually mattered —
/// whether anything in the mod declares it. These do that, and they are cheap
/// because the mod already carries its own manifest in the form of
/// <see cref="ScriptedEntityAttribute"/>.
/// </para>
/// </summary>
public sealed class ScriptedEntityKindTests
{
    private static ScriptedEntityAttribute[] Declared() =>
        typeof(SourceSharp.Server.ServerModule).Assembly
            .GetTypes()
            .SelectMany(t => t.GetCustomAttributes<ScriptedEntityAttribute>())
            .ToArray();

    /// <summary>
    /// Every kind the enum offers is claimed by at least one entity.
    ///
    /// This is the test the Surface gap fails. It is deliberately a test about
    /// the WHOLE mod rather than about the enum: a kind is a promise that some
    /// class of entity needs those four install values, and a promise nothing
    /// takes up is a branch in the native switch nobody will ever exercise.
    /// </summary>
    [Fact]
    public void EveryKindIsDeclaredBySomething()
    {
        var used = Declared().Select(a => a.Kind).ToHashSet();

        var unused = Enum.GetValues<ScriptedEntityKind>()
                         .Where(k => !used.Contains(k))
                         .OrderBy(k => (int)k)
                         .ToArray();

        Assert.True(
            unused.Length == 0,
            "these kinds exist and nothing declares them, so nothing can be "
            + "checking whether their native branch is right: "
            + string.Join(", ", unused.Select(k => $"{k} = {(int)k}")));
    }

    /// <summary>
    /// The numbers themselves, which cross the ABI to
    /// SourceSharp_RegisterScriptedEntity and are read as a bare int on the far
    /// side. Renumbering silently turns every beam into a logic entity.
    /// </summary>
    [Fact]
    public void KindNumbersAreFixed()
    {
        Assert.Equal(0, (int)ScriptedEntityKind.Logical);
        Assert.Equal(1, (int)ScriptedEntityKind.Item);
        Assert.Equal(2, (int)ScriptedEntityKind.Brush);
        Assert.Equal(3, (int)ScriptedEntityKind.Prop);
        Assert.Equal(5, (int)ScriptedEntityKind.Projectile);
        Assert.Equal(6, (int)ScriptedEntityKind.Effect);
        Assert.Equal(7, (int)ScriptedEntityKind.Player);
    }

    /// <summary>
    /// Exactly one class is the player. The kind selects the native player
    /// shell, and a second class declaring it would be a second entity wearing
    /// the engine's per-client machinery — a mistake that would otherwise
    /// surface as two factories fighting over connecting clients.
    /// </summary>
    [Fact]
    public void ExactlyOneClassIsThePlayer()
    {
        var players = Declared()
            .Where(a => a.Kind == ScriptedEntityKind.Player)
            .Select(a => a.ClassName)
            .ToArray();

        Assert.True(
            players.Length == 1 && players[0] == EntityNames.SsPlayer,
            "the Player kind must be declared by exactly ss_player, found: "
            + string.Join(", ", players));
    }

    /// <summary>
    /// 4 stays retired. Reusing a number an older managed build might still
    /// send is the one way this enum can break something that is not rebuilt
    /// alongside it, so the gap is asserted rather than merely commented.
    /// </summary>
    [Fact]
    public void FourIsNotReused()
    {
        Assert.False(
            Enum.IsDefined(typeof(ScriptedEntityKind), 4),
            "4 was Surface. It is reserved, not free: an old build sending it "
            + "must reach the native fallback, not a kind that has moved in.");
    }

    /// <summary>
    /// Every declared entity names a kind the enum actually has. A cast from an
    /// int is legal C# and would register a class the native side answers for
    /// with its Logical fallback — solid nothing, server-only, never drawn.
    /// </summary>
    [Fact]
    public void NoEntityDeclaresAKindThatDoesNotExist()
    {
        var bad = Declared()
            .Where(a => !Enum.IsDefined(typeof(ScriptedEntityKind), a.Kind))
            .Select(a => $"{a.ClassName} = {(int)a.Kind}")
            .OrderBy(s => s)
            .ToArray();

        Assert.True(bad.Length == 0, string.Join(", ", bad));
    }

    /// <summary>
    /// func_breakable_surf, named because it is the reason Surface was dead.
    /// Glass is a brush: it is a piece of the map, it collides as its submodel,
    /// and what shatters a pane at a time is PaneGrid.cs rather than anything
    /// the kind selects.
    /// </summary>
    [Fact]
    public void BreakableSurfIsABrush()
    {
        var surf = Declared()
            .SingleOrDefault(a => string.Equals(a.ClassName,
                                                EntityNames.FuncBreakableSurf,
                                                StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(surf);
        Assert.Equal(ScriptedEntityKind.Brush, surf!.Kind);
    }
}
