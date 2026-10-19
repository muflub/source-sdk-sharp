using SourceSharp.Abi;
using SourceSharp.Client;
using Debug.Api.Client.Interfaces;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's server-bounded convars (plan_client_novalve C8): stock's
/// cdll_bounded_cvars.cpp rules, decided in C# and pushed to the native
/// objects the engine reads through their vtable. Each fact is stock's own
/// expression with its edges; ClientBoundedConVarOracleTests pins those
/// expressions to the pristine file.
/// </summary>
public class BoundedConVarsTests
{
    // ---- the rules ------------------------------------------------------

    [Fact]
    public void PredictFollowsTheServerUnlessItSaysMinusOne()
    {
        Assert.Equal(1f, BoundedConVarRules.Predict(1f, -1));     // the server leaves it to the client
        Assert.Equal(0f, BoundedConVarRules.Predict(1f, 0));      // the server turns it off
        Assert.Equal(1f, BoundedConVarRules.Predict(0f, 1));      // ... or on
        Assert.Equal(0.5f, BoundedConVarRules.Predict(0.5f, null)); // no such convar: the client's
    }

    [Fact]
    public void InterpRatioClampsToTheServersBoundsUnlessTheMinimumIsMinusOne()
    {
        Assert.Equal(2f, BoundedConVarRules.InterpRatio(2f, -1f, 5f));
        Assert.Equal(3f, BoundedConVarRules.InterpRatio(2f, 3f, 5f));
        Assert.Equal(5f, BoundedConVarRules.InterpRatio(9f, 3f, 5f));
        Assert.Equal(4f, BoundedConVarRules.InterpRatio(4f, 3f, 5f));
        Assert.Equal(1f, BoundedConVarRules.InterpRatio(4f, 3f, 1f));    // mathlib clamp: max below min answers max
        Assert.Equal(2f, BoundedConVarRules.InterpRatio(2f, null, 5f));  // either bound missing: the client's
        Assert.Equal(2f, BoundedConVarRules.InterpRatio(2f, 1f, null));
    }

    [Fact]
    public void InterpIsAtLeastTheMinimumRatioOverTheBoundedUpdateRate()
    {
        Assert.Equal(0.1f, BoundedConVarRules.Interp(0.1f, 1f, 20f));     // 1/20 = 0.05 < 0.1
        Assert.Equal(0.15f, BoundedConVarRules.Interp(0.1f, 3f, 20f), 6); // 3/20
        Assert.Equal(0.1f, BoundedConVarRules.Interp(0.1f, -1f, 20f));    // minimum -1: the client's
        Assert.Equal(0.1f, BoundedConVarRules.Interp(0.1f, 1f, null));    // no update rate
    }

    [Fact]
    public void TheUpdateRateIsTheEnginesClampToTheServersBounds()
    {
        Assert.Equal(20f, BoundedConVarRules.UpdateRate(20f, 10f, 60f));
        Assert.Equal(10f, BoundedConVarRules.UpdateRate(5f, 10f, 60f));
        Assert.Equal(60f, BoundedConVarRules.UpdateRate(100f, 10f, 60f));
        Assert.Equal(20f, BoundedConVarRules.UpdateRate(20f, null, 60f));
    }

    [Fact]
    public void TheInterpolationAmountIsTheLargerOfInterpAndRatioOverRate()
    {
        Assert.Equal(0.1f, BoundedConVarRules.InterpAmount(0.1f, 2f, 66f));
        Assert.Equal(0.1f, BoundedConVarRules.InterpAmount(0.05f, 2f, 20f), 6);
        Assert.Equal(0.1f, BoundedConVarRules.InterpAmount(0.3f, 2f, null));   // no update rate: stock's 0.1
    }

    // ---- the push -------------------------------------------------------

    private sealed class Slots : IClientConVarSlots
    {
        public readonly Dictionary<string, int> Watched = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, float> Values = new(StringComparer.OrdinalIgnoreCase);
        public int Watch(string name) => Watched.TryGetValue(name, out int id) ? id : Watched[name] = 0x10000 + Watched.Count;
        public bool SetValue(string name, float value) { Values[name] = value; return true; }
    }

    private static (BoundedConVars Bounded, Slots Slots, Dictionary<string, string> Vars) Rig()
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cl_predict"] = "1", ["cl_interp"] = "0.1", ["cl_interp_ratio"] = "2", ["cl_updaterate"] = "20",
            ["sv_client_predict"] = "-1", ["sv_client_min_interp_ratio"] = "1", ["sv_client_max_interp_ratio"] = "5",
            ["sv_minupdaterate"] = "10", ["sv_maxupdaterate"] = "60",
        };
        var slots = new Slots();
        return (new BoundedConVars(slots, name => vars.TryGetValue(name, out string? v) ? v : null), slots, vars);
    }

    [Fact]
    public void StartWatchesEveryInputAndPushesAllFour()
    {
        var (bounded, slots, _) = Rig();
        bounded.Start();

        Assert.Equal(9, slots.Watched.Count);
        Assert.Equal(1f, slots.Values["cl_predict"]);
        Assert.Equal(2f, slots.Values["cl_interp_ratio"]);
        Assert.Equal(0.1f, slots.Values["cl_interp"]);
        Assert.Equal(0.1f, slots.Values[BoundedConVars.InterpAmountName], 6);
    }

    [Fact]
    public void AWatchedChangeRecomputesAndPushes()
    {
        var (bounded, slots, vars) = Rig();
        bounded.Start();

        vars["sv_client_predict"] = "0";
        Assert.True(bounded.OnChanged(slots.Watched["sv_client_predict"]));
        Assert.Equal(0f, slots.Values["cl_predict"]);

        vars["sv_client_min_interp_ratio"] = "4";
        Assert.True(bounded.OnChanged(slots.Watched["sv_client_min_interp_ratio"]));
        Assert.Equal(4f, slots.Values["cl_interp_ratio"]);
        Assert.Equal(0.2f, slots.Values["cl_interp"], 6);     // 4 / 20
    }

    [Fact]
    public void AnIdItDidNotWatchIsNotItsOwn()
    {
        var (bounded, slots, _) = Rig();
        bounded.Start();
        slots.Values.Clear();

        Assert.False(bounded.OnChanged(42));
        Assert.Empty(slots.Values);
    }
}
