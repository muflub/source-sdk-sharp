extern alias client;

using SourceSharp.Abi;

using Xunit;

using Adapter = client::TF2.Client.Ui.TFHudDamageIndicator;
using Core = client::SourceSharp.Client.IDamageListener;
using Clock = client::SourceSharp.Abi.IGameClock;
using DamageArgs = client::SourceSharp.Shared.DamageArgs;
using ICurrentView = client::SourceSharp.Client.Interop.ICurrentView;
using CurrentViewState = client::SourceSharp.Client.Interop.CurrentViewState;
using IFxRender = client::SourceSharp.Client.Interop.IFxRender;
using FxView = client::SourceSharp.Client.Interop.FxView;

// The contract assembly is unaliased: the record and the interface are
// global, as the building status's are.

namespace TF2.Tests.Client;

/// <summary>
/// The damage indicator's reads (stock's <c>CHudDamageIndicator</c>,
/// tf_hud_damageindicator.cpp): each Damage user message a hit whose scale
/// is what was dealt (the armour save plus the health take — stock's
/// <c>WRITE_SHORT(info.GetDamage())</c>, tf_player.cpp:9525), capped at the
/// MaximumDamage 100; a hit with no origin and the server's empty reports
/// record nothing (<c>MsgFunc_Damage</c>'s <c>!ReadOneBit</c> arm :286, and
/// the core Hud's rule); the show time remaps off the amount
/// (:297); the direction resolves against the frame's view
/// (<c>GetDamagePosition</c> :150-177); the expired leave and the clock
/// going back purges (a new level, <c>Reset</c>).
/// </summary>
public sealed class TFHudDamageIndicatorTests
{
    private sealed class RigClock : Clock
    {
        public float Now { get; set; } = 10f;
    }

    private sealed class RigView : ICurrentView
    {
        private int _id = 1;
        public Vector Origin = new(0f, 0f, 64f);
        public Vector Forward = new(1f, 0f, 0f);

        /// <summary>A new frame's view: the id moves so the adapter resolves.</summary>
        public void Turn(Vector forward)
        {
            Forward = forward;
            _id++;
        }

        public CurrentViewState Read() => new(_id, Origin, Forward, new Vector(-Forward.Y, Forward.X, 0f), new Vector(0f, 0f, 1f));
    }

    /// <summary>The fx seam's reads: the main view the message-time delta
    /// is taken against, and the render's current view beneath it — two
    /// answers a fact tells apart.</summary>
    private sealed class RigFx : IFxRender
    {
        public Vector MainViewOrigin() => new(-40f, 0f, 0f);

        public FxView View() => new(new Vector(0f, 0f, 64f), new Vector(1f, 0f, 0f), 1280);

        public int MaterialFind(string name) => 0;

        public void MaterialAddRef(int material) { }

        public void MaterialRelease(int material) { }

        public string MaterialName(int material) => string.Empty;

        public void DrawQuads(int material, ReadOnlySpan<client::SourceSharp.Client.Interop.FxVertex> vertices, bool byteColor) { }

        public int LocalPlayer() => 2;

        public QAngle ViewAngles() => new(0f, 0f, 0f);

        public int EntityAttachment(int entIndex, int attachment, out Vector origin, out string model)
        {
            origin = default;
            model = string.Empty;
            return 0;
        }

        public void DispatchParticleEffect(int systemIndex, Vector start, Vector end, QAngle angles, int entIndex) { }

        public bool EntityRenderAngles(int entIndex, out QAngle angles)
        {
            angles = default;
            return false;
        }

        public void EjectBrass(Vector origin, QAngle angles, QAngle gunAngles, int type) { }
    }

    /// <summary>A fake engine random: each draw pops the next answer, the
    /// last one repeating (the three per-axis draws, in x, y, z order).</summary>
    private sealed class RigRandom(params float[] draws) : client::SourceSharp.Abi.IUniformRandom
    {
        private int _next;

        public float RandomFloat(float min, float max)
        {
            int i = _next++;
            return draws.Length == 0 ? 0f : draws[Math.Min(i, draws.Length - 1)];
        }

        public int RandomInt(int min, int max) => min;
    }

    private sealed class Rig
    {
        public readonly RigClock Clock = new();
        public readonly RigView View = new();
        public readonly RigFx Fx = new();
        public readonly RigRandom Random = new();
        public readonly Adapter Source;

        public Rig(params float[] draws)
        {
            Random = new RigRandom(draws);
            Source = new Adapter(Clock, View, Random, Fx);
        }

        /// <summary>One Damage message through the core Hud's hook: what was
        /// saved to armour, what was taken in health, and where from.</summary>
        public void Hit(byte save, byte take, Vector? origin = null)
            => ((Core)Source).OnDamage(new DamageArgs(save, take, 0, origin ?? new Vector(64f, 0f, 64f)));
    }

    [Fact]
    public void A_hit_with_an_origin_records_one_hit_its_show_time_off_the_amount()
    {
        var r = new Rig();
        r.Hit(0, 30);

        var hit = Assert.Single(Enumerable.Range(0, r.Source.Count).Select(r.Source.Damage));
        Assert.Equal((30, 10f, 1.3f), (hit.Amount, hit.At, hit.Life));
    }

    [Fact]
    public void The_scale_is_what_was_dealt_capped_at_the_maximum()
    {
        var r = new Rig();
        r.Hit(40, 10);      // dealt 50: the save plus the take

        Assert.Equal(50, r.Source.Damage(0).Amount);

        var big = new Rig();
        big.Hit(0, 255);    // the byte's top, past the cap

        Assert.Equal(100, big.Source.Damage(0).Amount);
    }

    [Fact]
    public void The_servers_empty_reports_record_nothing()
    {
        var r = new Rig();
        r.Hit(0, 0);

        Assert.Equal(0, r.Source.Count);
    }

    [Fact]
    public void A_hit_with_no_origin_records_nothing()
    {
        var r = new Rig();
        r.Hit(0, 30, origin: new Vector(0f, 0f, 0f));

        Assert.Equal(0, r.Source.Count);
    }

    [Fact]
    public void Expired_hits_leave_and_a_new_level_purges_the_rest()
    {
        var r = new Rig();
        r.Hit(0, 30);                       // shows 1.3 s

        r.Clock.Now = 11f;
        Assert.Equal(1, r.Source.Count);

        r.Clock.Now = 12f;                  // past At + Life
        Assert.Equal(0, r.Source.Count);

        r.Hit(0, 30);
        r.Clock.Now = 5f;                   // the clock going back: a new level
        Assert.Equal(0, r.Source.Count);
    }

    [Fact]
    public void The_direction_resolves_against_the_frames_view()
    {
        var r = new Rig();
        r.Hit(0, 30, origin: new Vector(-40f, 64f, 64f)); // +Y of the main view: to its left

        Assert.Equal(1, r.Source.Count);                  // the frame's read resolves
        Assert.Equal(-90f, r.Source.Damage(0).Yaw, 2f);

        r.View.Turn(new Vector(0f, 1f, 0f));              // turned to face it
        Assert.Equal(1, r.Source.Count);
        Assert.Equal(0f, r.Source.Damage(0).Yaw, 2f);
    }

    [Fact]
    public void The_noise_draws_once_per_axis_and_the_bearing_is_the_renormalized_delta()
    {
        // Stock: delta = normalize(origin - view); delta[x,y,z] +=
        // RandomFloat(-Noise, Noise) in that order; normalize again; the
        // paint's angle is the frame's yaw minus the delta's (:304-311,
        // :161-162). The view sees the hit straight ahead, so the unfixed
        // bearing is 0 and the draws alone move it.
        var r = new Rig(0.1f, -0.1f, 0.05f);
        r.Hit(0, 30, origin: new Vector(60f, 0f, 0f));    // 100 along +X of the main view

        var d = new Vector(1f, 0f, 0f);
        d += new Vector(0.1f, -0.1f, 0.05f);
        d = d.Normalized;
        float yaw = -MathF.Atan2(d.Y, d.X) * (180f / MathF.PI);
        yaw = ((yaw % 360f) + 540f) % 360f - 180f;

        Assert.Equal(1, r.Source.Count);
        Assert.Equal(yaw, r.Source.Damage(0).Yaw, 5f);
        Assert.NotEqual(0f, r.Source.Damage(0).Yaw, 5f);  // the draws reached the bearing
    }

    [Fact]
    public void The_damage_delta_is_taken_from_the_main_view()
    {
        // The message can land outside the render's access window: the
        // delta of the moment is `vecFrom - MainViewOrigin()` (stock's
        // tf_hud_damageindicator arm, :357), the fx seam's origin — not the
        // render's current view the window guards. The hit sits 100 along
        // +Y of the main view (-40, 0, 0), so the bearing is -45 against
        // the +X frame; the current view's origin would give -59.
        var r = new Rig(0f, 0f, 0f);
        r.Hit(0, 30, origin: new Vector(60f, 100f, 0f));

        Assert.Equal(1, r.Source.Count);
        Assert.Equal(-45f, r.Source.Damage(0).Yaw, 1f);
    }

    [Fact]
    public void A_noise_of_none_leaves_the_bearing_the_raw_delta_s()
    {
        var r = new Rig(0f, 0f, 0f);
        r.Hit(0, 30, origin: new Vector(60f, 0f, 0f));    // 100 along +X of the main view

        Assert.Equal(0f, r.Source.Damage(0).Yaw, 5f);     // (1, 0, 0) against +X

        r.View.Turn(new Vector(0f, 1f, 0f));
        Assert.Equal(1, r.Source.Count);                  // the frame's read resolves
        Assert.Equal(90f, r.Source.Damage(0).Yaw, 5f);    // still the same frozen delta
    }
}
