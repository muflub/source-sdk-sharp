extern alias client;

using SourceSharp.Abi;

using Xunit;

using Engine = client::SourceSharp.Abi.RecordingEngine;
using Mirror = client::TF2.Client.Buildings.ClientTFSentryGun;
using Wire = client::TF2.Shared.Buildings.TFObjectNetState;

namespace TF2.Tests.Client;

/// <summary>
/// The sentry's shadow relay (<see cref="Mirror"/>): while the wire says the
/// building is placing, its shadow is cast from the collision hull widened by
/// (10, 10, 0) — stock's placing arm of
/// <c>C_ObjectSentrygun::GetShadowRenderBounds</c> (c_obj_sentrygun.cpp:588) —
/// and once built, from the model bounds again. The relay is the client half
/// of that: it reads the networked flag, not a local guess.
/// </summary>
public sealed class ClientBuildingShadowTests
{
    private static readonly EntityHandle Sentry = new(0x0002_0021u);

    private static Wire Placing(bool placing)
    {
        var state = new Wire();
        state.BindLocal();
        state.Placing = placing;
        return state;
    }

    /// <summary>A sentry that has decoded <paramref name="state"/> off the wire.</summary>
    private static (Mirror Gun, Engine Rig) Decoded(Wire state)
    {
        var engine = new Engine();
        var gun = new Mirror(engine);
        gun.Handle = Sentry;
        gun.State.BindLocal();
        gun.State.Decode(state.Snapshot());
        return (gun, engine);
    }

    [Fact]
    public void ABlueprintCastsItsShadowFromTheCollisionHull()
    {
        var (gun, engine) = Decoded(Placing(placing: true));

        gun.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        var hull = engine.Last("EntityRender.SetShadowHull");
        Assert.NotNull(hull);
        Assert.Equal(Sentry, hull!.Value.ArgAs<EntityHandle>(0));
        Assert.True(hull.Value.ArgAs<bool>(1), "the placing sentry's shadow is the widened hull");
    }

    [Fact]
    public void AnUnchangedWireDoesNotReprojectTheShadow()
    {
        var (gun, engine) = Decoded(Placing(placing: true));

        gun.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        gun.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        Assert.Single(engine.Calls, c => c.Member == "EntityRender.SetShadowHull");
    }

    [Fact]
    public void ABuiltSentryLeavesTheHullArm()
    {
        var (gun, engine) = Decoded(Placing(placing: true));
        gun.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        gun.State.Decode(Placing(placing: false).Snapshot());
        gun.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        var calls = engine.Calls.Where(c => c.Member == "EntityRender.SetShadowHull").ToList();
        Assert.Equal(2, calls.Count);
        Assert.False(calls[1].ArgAs<bool>(1), "the built sentry's shadow is the model's again");
    }
}
