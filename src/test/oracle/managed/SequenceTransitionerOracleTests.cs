using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using SourceSharp.Shared.Bones;
using SourceSharp.Tests;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// The managed <see cref="SequenceTransitioner"/> replayed against stock's
/// <c>CSequenceTransitioner</c>, step by step.
///
/// <para>
/// The golden, <c>Fixtures/sequence-transitioner-stock.tsv</c>, is what
/// pristine <c>game/shared/sequence_Transitioner.cpp</c>, on the client's
/// pristine <c>C_AnimationLayer</c> and <c>public/studio.cpp</c>'s
/// <c>CStudioHdr</c>, writes over a scripted run: changes, restarts, a
/// STUDIO_SNAP sequence, no-interp frames, fades and their trims, a clock
/// running backwards, RemoveAll, then a seeded random run of the same
/// operations. <c>sequence_transitioner_oracle_test</c> (the oracle-native
/// leg) holds stock to that file; this holds the port to it. Each line is one
/// operation and the queue after it: every entry's sequence, cycle, rate,
/// weight, animtime and fade time, the floats compared as bits.
/// </para>
/// </summary>
public class SequenceTransitionerOracleTests
{
    private const string Golden = "src/test/oracle/managed/Fixtures/sequence-transitioner-stock.tsv";

    private sealed record ModelRow(int Sequence, string Label, int Flags, float FadeIn, float FadeOut);

    private sealed record Step(int Number, string Op, string[] Args, string Queue, string Line);

    private static (List<ModelRow> Model, List<Step> Steps) Read()
    {
        string path = Path.Combine(ValveSdk.RepoRoot, Golden);
        Assert.True(File.Exists(path), $"{Golden} is missing: sequence_transitioner_oracle_test records it");

        var model = new List<ModelRow>();
        var steps = new List<Step>();

        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            string[] f = line.Split('\t');

            if (f[0] == "model")
            {
                model.Add(new ModelRow(int.Parse(f[1], CultureInfo.InvariantCulture), f[2],
                                       int.Parse(f[3], CultureInfo.InvariantCulture),
                                       float.Parse(f[4], CultureInfo.InvariantCulture),
                                       float.Parse(f[5], CultureInfo.InvariantCulture)));
                continue;
            }

            string[] args = f[2].Length == 0 ? [] : f[2].Split(' ');
            steps.Add(new Step(int.Parse(f[0], CultureInfo.InvariantCulture), f[1], args,
                               string.Join('\t', f, 3, f.Length - 3), line));
        }

        return (model, steps);
    }

    /// <summary>The golden's model as a studio header: one bone, and per row a
    /// sequence with that label, flags and fades over a one-frame anim. The
    /// transitioner reads only the flags and fades.</summary>
    private static StudioHdr Build(List<ModelRow> model)
    {
        var b = new StudioHdrBuilder();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1 });

        foreach (ModelRow row in model)
        {
            b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = row.Label, NumFrames = 1, Fps = 30f });
            b.Sequences.Add(new StudioHdrBuilder.SeqDef
            {
                Label = row.Label,
                AnimTable = [(short)row.Sequence],
                Flags = row.Flags,
                FadeInTime = row.FadeIn,
                FadeOutTime = row.FadeOut,
                Weights = [1f],
            });
        }

        Assert.True(StudioHdr.TryCreate(b.Build(), null, out StudioHdr? hdr) && hdr is not null,
                    "the golden's model was rejected");
        return hdr!;
    }

    private static string Bits(float f) =>
        BitConverter.SingleToUInt32Bits(f).ToString("x8", CultureInfo.InvariantCulture);

    /// <summary>The queue as the native harness writes it.</summary>
    private static string Queue(SequenceTransitioner t)
    {
        var s = new StringBuilder(t.Count.ToString(CultureInfo.InvariantCulture));

        for (int i = 0; i < t.Count; i++)
        {
            TransitionBlend e = t[i];
            s.Append('\t').Append(e.Sequence.ToString(CultureInfo.InvariantCulture))
             .Append(':').Append(Bits(e.Cycle)).Append(':').Append(Bits(e.PlaybackRate))
             .Append(':').Append(Bits(e.Weight)).Append(':').Append(Bits(e.LayerAnimtime))
             .Append(':').Append(Bits(e.LayerFadeOuttime));
        }

        return s.ToString();
    }

    private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static float Float(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    [Fact]
    public void EveryStepLeavesTheQueueStockLeaves()
    {
        (List<ModelRow> model, List<Step> steps) = Read();
        StudioHdr hdr = Build(model);
        SequenceTransitioner t = new();
        int compared = 0;

        foreach (Step step in steps)
        {
            switch (step.Op)
            {
            case "reset":
                t = new SequenceTransitioner();
                break;
            case "check":
                t.CheckForSequenceChange(hdr, Int(step.Args[0]), step.Args[1] == "1", step.Args[2] == "1");
                break;
            case "update":
                t.UpdateCurrent(hdr, Int(step.Args[0]), Float(step.Args[1]), Float(step.Args[2]), Float(step.Args[3]));
                break;
            case "removeall":
                t.RemoveAll();
                break;
            default:
                Assert.Fail($"step {step.Number}: an operation the replay does not know: '{step.Op}'");
                break;
            }

            string ours = Queue(t);
            Assert.True(ours == step.Queue,
                        $"step {step.Number} ({step.Op} {string.Join(' ', step.Args)}): stock left\n  {step.Queue}\nthe port left\n  {ours}");
            compared++;
        }

        // The stimulus reached the subject: the whole script was compared.
        Assert.True(compared > 5000, $"only {compared} steps were compared; the golden is truncated");
    }

    [Fact]
    public void TheReplayedModelHasTheGoldensFlagsAndFades()
    {
        (List<ModelRow> model, _) = Read();
        StudioHdr hdr = Build(model);

        Assert.True(model.Count == 5 && hdr.NumSequences == model.Count,
                    $"the golden names {model.Count} sequences and the replayed model has {hdr.NumSequences}");

        foreach (ModelRow row in model)
        {
            MstudioSeqDesc d = hdr.SeqDesc(row.Sequence);
            Assert.True(d.Flags == row.Flags && d.FadeInTime == row.FadeIn && d.FadeOutTime == row.FadeOut,
                        $"sequence {row.Sequence} ({row.Label}) reads back flags {d.Flags} fades {d.FadeInTime}/{d.FadeOutTime}, " +
                        $"the golden {row.Flags} {row.FadeIn}/{row.FadeOut}");
        }

        Assert.True((hdr.SeqDesc(2).Flags & StudioConstants.STUDIO_SNAP) != 0, "the snap sequence carries STUDIO_SNAP");
    }
}
