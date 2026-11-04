using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>The render plan's convars, read in managed.</summary>
public class RenderConVarsTests
{
    [Theory]
    [InlineData("1 2 3", 1f, 2f, 3f)]
    [InlineData("  10\t20  30 40", 10f, 20f, 30f)]
    [InlineData("1 2", 1f, 2f, -1f)]
    [InlineData("1 x 3", 1f, -1f, -1f)]
    [InlineData("nonsense", -1f, -1f, -1f)]
    [InlineData("", -1f, -1f, -1f)]
    public void AFogColourIsReadAsStocksSscanfReadIt(string text, float r, float g, float b)
        => Assert.Equal(new Vector(r, g, b), RenderConVars.ParseColor(text));

    [Fact]
    public void AFogColourWithNoTextIsNoColour()
        => Assert.Equal(new Vector(-1f, -1f, -1f), RenderConVars.ParseColor(null));

    [Fact]
    public void TheFogOverrideFollowsAChangeOfItsColourConVar()
    {
        RenderConVars convars = RenderPlannerTests.ConVars();
        RenderInputs input = RenderPlannerTests.Inputs();
        convars.Set("fog_override", 1f);

        convars.SetText("fog_color", "255 0 0");
        Vector red = FogMath.FogColor(input, convars, new Vector(1f, 0f, 0f));
        convars.SetText("fog_color", "0 255 0");
        Vector green = FogMath.FogColor(input, convars, new Vector(1f, 0f, 0f));

        Assert.Equal(new Vector(1f, 0f, 0f), red);
        Assert.Equal(new Vector(0f, 1f, 0f), green);
    }

    [Fact]
    public void APlanFollowsAConVarsChangeWithNoNewHolder()
    {
        RenderConVars convars = RenderPlannerTests.ConVars();
        RenderInputs input = RenderPlannerTests.Inputs();
        int ViewModelSwitch() => RenderPlanner.Plan(input, convars)!.Single(o => o.Kind == (int)RenderOpKind.ViewModels).GetI(0);

        int with = ViewModelSwitch();
        convars.Set("r_drawviewmodel", 0f);
        int without = ViewModelSwitch();

        Assert.Equal((1, 0), (with, without));
    }

    [Fact]
    public void ThePostProgramFollowsAConVarsChange()
    {
        var convars = RenderConVarsKit.Zeroed<PostProcessConVars>();
        var planner = new PostProcessPlanner(() => "mods/test", convars);
        var input = new PostProcessInputs { Kind = (int)PostProgramKind.MotionBlur, HardwareDxLevel = 95, Width = 64, Height = 64 };

        int Ops()
        {
            var builder = new RenderPlanBuilder();
            planner.Plan(input, new PostOpsWriter(builder, _ => 1), new PostProgramEffects());
            return builder.Ops.Count;
        }

        int off = Ops();
        convars.Set("mat_motion_blur_enabled", 1f);
        int on = Ops();

        Assert.True(on > off, $"mat_motion_blur_enabled on should draw a pass: {on} against {off}");
    }
}
