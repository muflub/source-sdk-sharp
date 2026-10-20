using System;
using System.Collections.Generic;
using System.Globalization;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A managed write reads the engine's value back into the cache, whether or
/// not the engine's change callback delivered it. The callback is dropped
/// while the managed module initialises, and MovementTuning.Apply writes then:
/// sv_gravity read 600 in the engine and 800 in C# from P2.3a on.
/// </summary>
public class ConVarWriteBackTests
{
    private sealed class FakeBackend : IConVarBackend
    {
        public string Held = "";
        public int Sets;
        public Func<string, string> Clamp = v => v;

        // Set by a test to model a loaded module: the callback arrives inside the set.
        public ConVarBase? DeliverTo;

        public void Set(int id, string value)
        {
            Sets++;
            string old = Held;
            Held = Clamp(value);
            if (DeliverTo is not null && Held != old)
                DeliverTo.NotifyChanged(Held, old);
        }

        public string Get(int id) => Held;
        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max) => 0;
        public string? ValueOf(string name) => Held;
    }

    private sealed class RecordingFloat : FloatConVar
    {
        public readonly List<(float Old, float New)> Changes = new();
        protected override void OnChanged(float oldValue, float newValue) => Changes.Add((oldValue, newValue));
    }

    private static (RecordingFloat Var, FakeBackend Backend) Bound(string engineValue)
    {
        var v = new RecordingFloat();
        var b = new FakeBackend { Held = engineValue };
        v.Backend = b;
        v.Bind(0, "ss_test_writeback", "800", engineValue: engineValue);
        v.Changes.Clear();
        return (v, b);
    }

    private static string Text(float f) => f.ToString(CultureInfo.InvariantCulture);

    [Fact]
    public void AFloatIsReadAsTheEngineReadsItWithATrailingF()
    {
        // Stock declares r_rainlength "0.1f", r_rainspeed "600.0f" and three
        // snow knobs the same way; the engine reads them with atof, which stops
        // at the 'f'. The managed cache read them as 0.
        var (v, _) = Bound("0.1f");
        var (w, _) = Bound("600.0f");

        Assert.True(v.Value == 0.1f && w.Value == 600.0f, $"0.1f -> {v.Value}, 600.0f -> {w.Value}");
    }

    [Fact]
    public void AFloatIsRoundedThroughADoubleAsAtofRoundsIt()
    {
        // The engine's carrier reads (float)atof (convar_port.cpp's
        // InternalSetValue): the text to the nearest double, then that double
        // to the nearest float. 1 + 2^-24 + 1.09e-19 is nearest the double
        // 1 + 2^-24, a tie between 1 and 1 + 2^-23 that rounds to even: 1.
        // Read straight to a float it is past the tie: 1 + 2^-23. The server's
        // movement read these convars natively until plan_server_native_v2 T4g,
        // so a managed read that rounds once is a different friction.
        var (v, _) = Bound("1.00000005960464477550");

        Assert.True(v.Value == 1.0f, $"(float)atof is 1, the cache read {v.Value:R}");
    }

    [Fact]
    public void AWriteWhoseCallbackWasDroppedStillReachesTheCache()
    {
        var (v, b) = Bound("800");

        v.SetValue(600f);

        Assert.Equal(1, b.Sets);
        Assert.Equal(b.Held, v.RawValue);
        Assert.Equal(600f, v.Value);
        Assert.Single(v.Changes);
        Assert.Equal((800f, 600f), v.Changes[0]);
    }

    [Fact]
    public void ACallbackThatDeliveredIsNotDeliveredTwice()
    {
        var (v, b) = Bound("800");
        b.DeliverTo = v;

        v.SetValue(600f);

        Assert.Single(v.Changes);
        Assert.Equal(600f, v.Value);
    }

    [Fact]
    public void TheCacheHoldsTheEnginesTextNotTheRequest()
    {
        var (v, b) = Bound("800");
        b.Clamp = r => float.Parse(r, CultureInfo.InvariantCulture) > 1000f ? "1000.000000" : r;

        v.SetValue(5000f);

        Assert.Equal("1000.000000", v.RawValue);
        Assert.Equal(1000f, v.Value);
    }

    [Fact]
    public void AWriteOfTheValueAlreadyHeldAnnouncesNothing()
    {
        var (v, b) = Bound(Text(600f));

        v.SetValue(600f);

        Assert.Empty(v.Changes);
    }

    [Fact]
    public void AnUnboundConVarWritesNowhere()
    {
        var v = new RecordingFloat();
        var b = new FakeBackend { Held = "800" };
        v.Backend = b;
        v.Bind(-1, "ss_test_refused", "800");

        v.SetValue(600f);

        Assert.Equal(0, b.Sets);
        Assert.Equal("800", v.RawValue);
    }
}
