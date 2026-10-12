using System;
using System.Linq;
using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_novalve.md P2.3: the movement tuning and its audit, in C#. The
/// classification facts are movement_audit_test.cpp's, carried across with the
/// logic (shell_movement_audit.cpp); the rest pin what the C# side now owns --
/// that the eight are declared here, audited, and fed from their own fields.
/// </summary>
public class MovementTuningTests
{
    private const float GravityRegistered = 600f;
    private const float GravityDeclared = 800f;

    [Fact]
    public void TheValueTheModSetIsNotAFault()
        => Assert.Equal(MovementTuning.Verdict.AsRegistered,
                        MovementTuning.Classify(GravityRegistered, GravityDeclared, GravityRegistered));

    [Fact]
    public void BackAtTheDeclaredDefaultIsTheDefect()
        => Assert.Equal(MovementTuning.Verdict.RevertedToDeclared,
                        MovementTuning.Classify(GravityRegistered, GravityDeclared, GravityDeclared));

    [Fact]
    public void AThirdNumberIsAnOverrideAndNotTheDefect()
        => Assert.Equal(MovementTuning.Verdict.Overridden,
                        MovementTuning.Classify(GravityRegistered, GravityDeclared, 700f));

    [Fact]
    public void WhenTheTuningIsTheDeclarationNothingIsReported()
        => Assert.Equal(MovementTuning.Verdict.AsRegistered, MovementTuning.Classify(10f, 10f, 10f));

    [Fact]
    public void TheDecimalStringRoundTripIsNotAChange()
        => Assert.Equal(MovementTuning.Verdict.AsRegistered,
                        MovementTuning.Classify(0.33333334f, 1f, 0.333333f));

    [Fact]
    public void AStopspeedRevertIsCaughtToo()
        => Assert.Equal(MovementTuning.Verdict.RevertedToDeclared, MovementTuning.Classify(75f, 100f, 100f));

    [Fact]
    public void TheTableIsTheEightConVarsTheTuningWrites()
        => Assert.Equal(new[]
           {
               "sv_gravity", "sv_friction", "sv_accelerate", "sv_airaccelerate",
               "sv_stopspeed", "sv_maxvelocity", "sv_stepsize", "sv_maxspeed",
           }, MovementTuning.Names);

    [Fact]
    public void EveryNameResolvesToItsOwnIndex()
    {
        for (int i = 0; i < MovementTuning.Names.Length; i++)
            Assert.Equal(i, MovementTuning.IndexOf(MovementTuning.Names[i]));
    }

    [Fact]
    public void AConVarTheTuningDoesNotWriteIsNotOurs()
    {
        Assert.Equal(-1, MovementTuning.IndexOf("sv_cheats"));
        Assert.Equal(-1, MovementTuning.IndexOf("sv_skyname"));
        Assert.Equal(-1, MovementTuning.IndexOf("sv_waterfriction"));
        Assert.Equal(-1, MovementTuning.IndexOf("SV_GRAVITY"));
    }

    [Fact]
    public void EachNameIsFedFromItsOwnField()
    {
        var p = new MovementParams
        {
            Gravity = 1, Friction = 2, Accelerate = 3, AirAccelerate = 4,
            StopSpeed = 5, MaxVelocity = 6, StepSize = 7, MaxSpeed = 8,
        };
        for (int i = 0; i < MovementTuning.Names.Length; i++)
            Assert.Equal(i + 1, MovementTuning.ValueFor(i, p));
    }

    [Fact]
    public void ExactlyTheEightAreDeclaredAsTunedConVars()
    {
        string[] tuned = typeof(MovementTunedConVar).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(MovementTunedConVar).IsAssignableFrom(t))
            .Select(t => t.GetCustomAttribute<ConVarAttribute>()!.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(MovementTuning.Names.OrderBy(n => n, StringComparer.Ordinal), tuned);
    }

    [Fact]
    public void AnUnappliedConVarIsNeverAudited()
    {
        // Audit before Apply has written the name: nothing to hold it against,
        // so it must not throw and must not report.
        new AppliedMovementTuning(new RecordingLog()).Audit("sv_gravity", "800", "800", 123f);
    }

    /// <summary>The warnings one audit printed.</summary>
    private static List<string> AuditWarnings(AppliedMovementTuning tuning, RecordingLog log, float now)
    {
        int before = log.Lines.Count;
        tuning.Audit("sv_gravity", "600", "800", now);
        return log.Lines.Skip(before).Where(l => l.Warning).Select(l => l.Text).ToList();
    }

    [Fact]
    public void AnAppliedConVarRevertedToItsDeclarationIsReported()
    {
        // The positive arm: the record remembers what it wrote, and a move back
        // to the declared default is the defect the audit exists for.
        var log = new RecordingLog();
        var tuning = new AppliedMovementTuning(log);
        tuning.Remember(MovementTuning.IndexOf("sv_gravity"), 600f);

        Assert.Contains(AuditWarnings(tuning, log, 800f), w => w.Contains("back at its DECLARED DEFAULT"));
    }

    [Fact]
    public void TwoRecordsDoNotShareWhatWasApplied()
    {
        // The property the static array lacked: one module's applied tuning
        // is not another module's.
        var mineLog = new RecordingLog();
        var mine = new AppliedMovementTuning(mineLog);
        var theirs = new AppliedMovementTuning(new RecordingLog());
        theirs.Remember(MovementTuning.IndexOf("sv_gravity"), 600f);

        Assert.Empty(AuditWarnings(mine, mineLog, 800f));
    }
}
