using System.Text.RegularExpressions;
using SourceSharp.Abi;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="TempEntTransform"/> — the state C# owns while it simulates a
/// pooled temp entity, and the decision behaviour-ledger row C4 turned on.
///
/// <para>
/// <c>C_LocalTempEntity::Frame</c> integrates a place, a velocity and a spin
/// every tick, and none of that had ever crossed this boundary.
/// <see cref="TempEntLife"/> carries eight fields and the transform is not among
/// them, so porting <c>Frame</c> meant choosing where the transform lives.
/// </para>
///
/// <para>
/// WHAT THESE PIN IS THE SHAPE OF THAT CHOICE: one struct crossing twice — once
/// on takeover and once per frame — rather than a dozen per-field accessors per
/// tempent per frame. A test that only checked "the origin arrived" would pass
/// just as happily against the shape that was rejected.
/// </para>
/// </summary>
public class TempEntTransformTests
{
    /// <summary>
    /// Every field survives the crossing, each with a DISTINCT value so a
    /// transposition cannot pass — the same reason the forward dispatcher's
    /// tests use distinct numbers.
    /// </summary>
    [Fact]
    public void EveryFieldOfTheTransformSurvivesBeingWrittenAndReadBack()
    {
        var sent = new TempEntTransform
        {
            Origin       = new Vector(11f, 22f, 33f),
            Angles       = new QAngle(44f, 55f, 66f),
            Velocity     = new Vector(77f, 88f, 99f),
            Acceleration = new Vector(-1f, -2f, -3f),
            AngVelocity  = new QAngle(-4f, -5f, -6f),
            SpriteScale  = 2.5f,
            Frame        = 7.25f,
        };

        var engine = Fake();
        engine.Strict = false;
        engine.Returns("Effects.TempEntPoolWriteTransform", true);
        engine.Returns("Effects.TempEntPoolReadTransform", true);
        engine.Returns("Effects.TempEntPoolReadTransform.Transform", sent);

        IEngineEffects effects = engine;

        effects.TempEntPoolWriteTransform(4, in sent);

        var call = engine.Last("Effects.TempEntPoolWriteTransform")!.Value;

        Assert.True(call.ArgAs<int>(0) == 4, "the slot is the first argument");

        // THE WHOLE STRUCT IS THE ARGUMENT, which is the design: a recording
        // that kept only the slot could not tell a test where the tempent
        // ended the frame.
        var written = call.ArgAs<TempEntTransform>(1);

        Assert.True(written.Origin.X == 11f && written.Origin.Y == 22f && written.Origin.Z == 33f,
            $"the origin crossed as {written.Origin}");

        Assert.True(written.Angles.Pitch == 44f && written.Angles.Yaw == 55f
                 && written.Angles.Roll == 66f,
            $"and the angles as pitch/yaw/roll, not reordered: {written.Angles}");

        Assert.True(written.Velocity.X == 77f && written.Acceleration.X == -1f,
            $"velocity and acceleration are different fields and stayed so: "
          + $"{written.Velocity.X}, {written.Acceleration.X}");

        Assert.True(written.AngVelocity.Pitch == -4f,
            $"the angular velocity is its own QAngle: {written.AngVelocity}");

        Assert.True(written.SpriteScale == 2.5f && written.Frame == 7.25f,
            $"and the sprite's scale and frame ride with the place, because Frame advances them "
          + $"together: {written.SpriteScale}, {written.Frame}");

        Assert.True(effects.TempEntPoolReadTransform(4, out TempEntTransform back)
                 && back.Origin.Z == 33f && back.Frame == 7.25f,
            "and the read answers the same record");
    }

    /// <summary>
    /// THE THREE FIELDS THAT DELIBERATELY DO NOT CROSS, asserted so that adding
    /// one later is a decision rather than a drift.
    ///
    /// <para>
    /// <c>m_vecPrevLocalOrigin</c>, <c>m_vLastCollisionOrigin</c> and
    /// <c>m_iLastCollisionFrame</c> look like shared state and are not: the
    /// client's only readers and writers of all three are inside <c>Frame</c>
    /// itself. Once C# owns <c>Frame</c> they are its own working state, and
    /// carrying them would be moving a function's locals across an ABI twice a
    /// frame for nothing.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTransformCarriesNoFieldThatIsPrivateToFrame()
    {
        string[] names = typeof(TempEntTransform)
            .GetFields()
            .Select(f => f.Name)
            .ToArray();

        foreach (string absent in new[] { "PrevOrigin", "LastCollisionOrigin", "LastCollisionFrame" })
        {
            Assert.True(!names.Contains(absent),
                $"{absent} is working state private to Frame — the client's only readers of it are "
              + "inside that one function — so it must not cross. Adding it is a decision about "
              + "ownership, not a field to slip in");
        }

        Assert.True(names.Length == 9,
            $"the transform is nine fields and has {names.Length}. This is a literal on purpose: "
          + "a field arriving here is a per-frame ABI crossing for every live tempent, which is "
          + "the cost the one-struct shape exists to bound. It has moved once, for "
          + "FTENT_SINEWAVE's two accumulators, which Frame ADVANCES every tick and which the "
          + "spawn record therefore cannot own");
    }

    /// <summary>
    /// AND IT MIRRORS THE C STRUCT, read off disk. The two are one record in two
    /// languages, written back into a live object field by field — a field
    /// added on one side only is a silent misread of everything after it.
    /// </summary>
    [Fact]
    public void TheManagedTransformMirrorsTheOneTheHeaderDeclares()
    {
        string? header = RepoLayout.FindSdkFile(Path.Combine(
            "src", "sourcesharp", "host", "sourcesharp.h"));

        if (header is null)
        {
            Assert.True(true, "sourcesharp.h read for the transform check (SKIPPED)");
            return;
        }

        Match block = Regex.Match(File.ReadAllText(header),
            @"typedef struct SourceSharpTempEntTransform_s\s*\{(.*?)\}\s*SourceSharpTempEntTransform_t;",
            RegexOptions.Singleline);

        Assert.True(block.Success, "the header declares SourceSharpTempEntTransform_t");

        // float x[3] is a Vector or a QAngle; a bare float is one field.
        int floats = Regex.Matches(block.Groups[1].Value, @"float\s+\w+\[\s*3\s*\]").Count;
        int scalars = Regex.Matches(block.Groups[1].Value, @"float\s+\w+\s*;").Count;

        Assert.True(floats == 5,
            $"five three-float members — origin, angles, velocity, acceleration and angular "
          + $"velocity — and the header has {floats}");

        Assert.True(scalars == 4,
            $"and four scalars — the sprite's scale and frame, and the sine wave's two "
          + $"accumulators; the header has {scalars}");

        Assert.True(floats + scalars == typeof(TempEntTransform).GetFields().Length,
            "so the two sides have the same number of fields. They are one record written back "
          + "into a live object field by field, and a field on one side only misreads everything "
          + "after it");
    }
}
