using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The C# collision rules produce the matrix native holds.
///
/// <para>
/// <b>This is one link of a chain, and the other link is in C++.</b>
/// collision_rules_test.cpp keeps stock's <c>ShouldCollide</c> VERBATIM and
/// proves the native default table answers what stock answered, for all four
/// hundred pairs. This file proves the C# predicate builds that same table. Put
/// together they say the C# rules are stock's rules, without either side having
/// to transcribe the other.
/// </para>
///
/// <para>
/// The direction matters: native is checked against stock, and C# is checked
/// against native. If C# were checked against a transcription of stock written
/// here, the transcription and the implementation would be two things I wrote
/// from the same reading, and a misreading would agree with itself.
/// </para>
/// </summary>
public class CollisionRulesTests
{
    private const string ShellCpp = "src/sourcesharp/game/shared/shell_gamerules.cpp";

    /// <summary>The twenty words out of the shell's default table.</summary>
    private static uint[] NativeRows()
    {
        var cpp = File.ReadAllText(Path.Combine(RepoLayout.SdkTree, ShellCpp));

        int at = cpp.IndexOf("static SourceSharpCollisionRules s_CollisionRules", StringComparison.Ordinal);
        Assert.True(at >= 0, "the native collision table is gone from " + ShellCpp);

        int end = cpp.IndexOf("};", at, StringComparison.Ordinal);

        return Regex.Matches(cpp[at..end], @"0x([0-9A-Fa-f]{8})u")
            .Select(m => uint.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .ToArray();
    }

    [Fact]
    public void TheBuiltMatrixIsTheNativeDefaultTable()
    {
        var native = NativeRows();
        var built = CollisionRules.BuildMatrix();

        Assert.True(native.Length == built.Length,
            "native holds " + native.Length + " row(s) and C# builds " + built.Length
            + ". One of the two has a different idea of how many collision groups "
            + "there are, and the matrix is indexed by that number.");

        for (int i = 0; i < native.Length; i++)
        {
            Assert.True(native[i] == built[i],
                "row " + i + " (" + (EngineCollisionGroup)i + "): native has 0x"
                + native[i].ToString("X8") + ", C# builds 0x" + built[i].ToString("X8")
                + ". The native words are proved against stock's own predicate by "
                + "collision_rules_test.cpp, so a difference here means the C# "
                + "rules have diverged from stock.");
        }
    }

    /// <summary>
    /// The row count is the group count, and the group count is what the blob
    /// carries. A mismatch here is caught at registration by SetRows throwing,
    /// but that is a runtime failure on a live server; this is the same fact
    /// checked at build time.
    /// </summary>
    [Fact]
    public void TheGroupCountMatchesTheBlobAndTheEnum()
    {
        Assert.Equal(CollisionRulesBlob.RowCount, CollisionRules.GroupCount);
        Assert.Equal((int)EngineCollisionGroup.Count, CollisionRules.GroupCount);
        Assert.Equal(CollisionRulesBlob.RowCount, CollisionRules.BuildMatrix().Length);
    }

    /// <summary>
    /// The relation is symmetric because the predicate sorts first. The native
    /// lookup does NOT sort, so this is what makes argument order irrelevant
    /// there.
    /// </summary>
    [Fact]
    public void TheRelationIsSymmetric()
    {
        for (int a = 0; a < CollisionRules.GroupCount; a++)
        {
            for (int b = a + 1; b < CollisionRules.GroupCount; b++)
            {
                Assert.True(
                    CollisionRules.ShouldCollide((EngineCollisionGroup)a, (EngineCollisionGroup)b)
                    == CollisionRules.ShouldCollide((EngineCollisionGroup)b, (EngineCollisionGroup)a),
                    (EngineCollisionGroup)a + " and " + (EngineCollisionGroup)b
                    + " answer differently depending on argument order. The native "
                    + "lookup does not sort its arguments, so it would give two "
                    + "answers for one pair.");
            }
        }
    }

    // ------------------------------------------------------------------
    // A few rules named in English, so a reader can check the table means
    // what the comments claim without decoding hex.
    // ------------------------------------------------------------------

    [Fact]
    public void APlayerPassesThroughPushawayPropsButDebrisDoesNot()
    {
        Assert.False(CollisionRules.ShouldCollide(EngineCollisionGroup.Player,
                                                  EngineCollisionGroup.PushAway));
        Assert.False(CollisionRules.ShouldCollide(EngineCollisionGroup.PlayerMovement,
                                                  EngineCollisionGroup.PushAway));

        // The exception, and it has to be tested BEFORE the blanket debris rule
        // or it would never be reached -- which is why order is load-bearing.
        Assert.True(CollisionRules.ShouldCollide(EngineCollisionGroup.Debris,
                                                 EngineCollisionGroup.PushAway));
    }

    [Fact]
    public void AnythingInAVehicleCollidesWithNothing()
    {
        for (int b = 0; b < CollisionRules.GroupCount; b++)
        {
            Assert.False(CollisionRules.ShouldCollide(EngineCollisionGroup.InVehicle,
                                                      (EngineCollisionGroup)b),
                "InVehicle collides with " + (EngineCollisionGroup)b);
        }
    }

    [Fact]
    public void AVehicleClipStopsVehiclesAndNothingElse()
    {
        Assert.True(CollisionRules.ShouldCollide(EngineCollisionGroup.VehicleClip,
                                                 EngineCollisionGroup.Vehicle));

        foreach (var other in new[] { EngineCollisionGroup.Player, EngineCollisionGroup.Npc,
                                      EngineCollisionGroup.None, EngineCollisionGroup.Projectile })
        {
            Assert.False(CollisionRules.ShouldCollide(EngineCollisionGroup.VehicleClip, other),
                "a vehicle clip stops " + other + ", so a player would walk into an "
                + "invisible wall meant only for cars.");
        }
    }

    [Fact]
    public void ADoorBlockerStopsEverythingExceptNpcs()
    {
        Assert.True(CollisionRules.ShouldCollide(EngineCollisionGroup.DoorBlocker,
                                                 EngineCollisionGroup.Npc));
        Assert.False(CollisionRules.ShouldCollide(EngineCollisionGroup.DoorBlocker,
                                                  EngineCollisionGroup.Player));
    }

    // ------------------------------------------------------------------
    // Registration.
    // ------------------------------------------------------------------

    [Fact]
    public void RegisterPushesTheBuiltMatrix()
    {
        var engine = new RecordingEngine();
        CollisionRules.Register(engine);

        var call = engine.Calls.Single(c => c.Member == "CollisionRules.RegisterCollisionRules");
        var rows = Assert.IsType<uint[]>(call.Arg(0));

        Assert.Equal(CollisionRules.BuildMatrix(), rows);
    }

    /// <summary>
    /// A matrix of the wrong size is refused rather than truncated. Sending the
    /// first twenty of twenty-two rows would leave the last two groups
    /// answering whatever the native defaults said, silently.
    /// </summary>
    [Fact]
    public void AMatrixOfTheWrongSizeIsRefused()
    {
        var blob = default(CollisionRulesBlob);

        Assert.Throws<ArgumentException>(() => blob.SetRows(new uint[CollisionRulesBlob.RowCount - 1]));
        Assert.Throws<ArgumentException>(() => blob.SetRows(new uint[CollisionRulesBlob.RowCount + 1]));
        Assert.Throws<ArgumentException>(() => blob.SetRows(null!));
    }

    /// <summary>Both sides register it — the client predicts with the same table.</summary>
    [Fact]
    public void BothModulesRegisterTheMatrix()
    {
        var root = RepoLayout.SdkTree;

        foreach (var module in new[] { "src/sourcesharp/managed/SourceSharp.Server/ServerModule.cs",
                                       "src/sourcesharp/managed/SourceSharp.Client/ClientModule.cs" })
        {
            var text = File.ReadAllText(Path.Combine(root, module));

            Assert.True(text.Contains("CollisionRules.Register(", StringComparison.Ordinal),
                module + " does not register the collision matrix. ShouldCollide is "
                + "asked on both sides -- util_shared.cpp is shared and the trace "
                + "filter runs during prediction -- so a side that does not register "
                + "answers from the native defaults, and a change to the C# rules "
                + "desynchronises prediction with nothing to report it.");
        }
    }
}
