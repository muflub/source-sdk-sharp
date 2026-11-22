extern alias client;

using System.Reflection;

using SourceSharp.Abi;

using Xunit;

using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientFurnitureEntity = client::TF2.Descent.Client.Furniture.ClientFurnitureEntity;
using ClientStandaloneEntity = client::SourceSharp.Client.Standalone.ClientStandaloneEntity;
using FurnitureCatalog = client::TF2.Descent.Shared.FurnitureCatalog;
using FurnitureFlags = client::TF2.Descent.Shared.FurnitureFlags;
using FurnitureKindId = client::TF2.Descent.Shared.FurnitureKindId;
using FurnitureNear = client::TF2.Descent.Client.Furniture.FurnitureNear;
using IClientLevelHook = client::SourceSharp.Client.IClientLevelHook;
using ClientCollisionGroup = client::SourceSharp.Abi.EngineCollisionGroup;
using ClientSideServiceAttribute = client::SourceSharp.Abi.SideServiceAttribute;
using IBreakPieceGroupRule = client::SourceSharp.Client.TempEnts.IBreakPieceGroupRule;
using TFBreakPieceGroup = client::TF2.Descent.Client.Furniture.TFBreakPieceGroup;

namespace TF2.Descent.Tests.Furniture;

/// <summary>
/// The client's furniture (plan_descent_logic WO3): the entities it holds, and what is near the
/// local hero for a use prompt (<c>IFurnitureNear</c>): kind, name token and state, nearest first,
/// no dormant one, nothing kept past the level.
/// </summary>
public sealed class FurnitureClientTests
{
    private static ClientEngine Engine(Vector local)
    {
        var engine = new ClientEngine();
        engine.Returns("Hud.LocalPlayerOrigin", (Vector?)local);
        return engine;
    }

    /// <summary>A client furniture entity at <paramref name="index"/>, as the registry would make it.</summary>
    private static ClientFurnitureEntity Entity(FurnitureNear near, ClientEngine engine, int index, FurnitureKindId kind, string variant,
                                                Vector at, FurnitureFlags flags = FurnitureFlags.None, bool dormant = false)
    {
        var e = new ClientFurnitureEntity(engine, near);
        var handle = new EntityHandle((uint)((7 << 16) | index));
        typeof(ClientStandaloneEntity).GetProperty("Index")!.SetValue(e, index);
        typeof(ClientStandaloneEntity).GetProperty("Handle")!.SetValue(e, handle);
        typeof(ClientStandaloneEntity).GetProperty("Entities", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(e, engine);
        e.State.BindLocal();
        e.State.Entry = FurnitureCatalog.Number(kind, variant);
        e.State.Flags = (byte)flags;
        e.State.Left = 255;
        engine.ReturnsEachCall("EntityRender.IsDormant", () => engine.Calls[^1].ArgAs<EntityHandle>(0) == handle ? dormant : DormantOthers);
        engine.ReturnsEachCall("Entities.GetOrigin", () => Places.TryGetValue(engine.Calls[^1].ArgAs<EntityHandle>(0), out var p) ? p : default);
        Places[handle] = at;
        e.OnSpawn();
        return e;
    }

    private static readonly Dictionary<EntityHandle, Vector> Places = [];
    private const bool DormantOthers = false;

    [Fact]
    public void The_furniture_near_the_local_hero_is_listed_nearest_first_with_its_name_and_state()
    {
        var engine = Engine(new Vector(0, 0, 0));
        var near = new FurnitureNear(engine);
        Entity(near, engine, 301, FurnitureKindId.Container, "supply_crate", new Vector(60, 0, 0), FurnitureFlags.Opened);
        Entity(near, engine, 302, FurnitureKindId.Shrine, "fury", new Vector(30, 0, 0));

        var list = near.Near(96);

        Assert.Equal([302, 301], list.Select(e => e.Entity));
        Assert.Equal((FurnitureKindId.Shrine, "fury", "#TF2Descent_Shrine_Fury", FurnitureFlags.None),
            (list[0].Kind, list[0].Variant, list[0].NameToken, list[0].Flags));
        Assert.Equal(FurnitureFlags.Opened, list[1].Flags);
        Assert.Equal(30, list[0].Distance, 3);
    }

    [Fact]
    public void Furniture_beyond_reach_is_not_near()
    {
        var engine = Engine(new Vector(0, 0, 0));
        var near = new FurnitureNear(engine);
        Entity(near, engine, 311, FurnitureKindId.Explosive, "barrel", new Vector(500, 0, 0));

        Assert.Empty(near.Near(96));
        Assert.Single(near.Near(600));
    }

    [Fact]
    public void The_nearest_usable_skips_what_use_does_not_answer()
    {
        var engine = Engine(new Vector(0, 0, 0));
        var near = new FurnitureNear(engine);
        Entity(near, engine, 321, FurnitureKindId.Explosive, "barrel", new Vector(20, 0, 0));
        Entity(near, engine, 322, FurnitureKindId.Container, "locker", new Vector(70, 0, 0));

        Assert.Equal(322, near.NearestUsable(96)!.Entity);
    }

    [Fact]
    public void A_dormant_entity_is_not_near()
    {
        var engine = Engine(new Vector(0, 0, 0));
        var near = new FurnitureNear(engine);
        Entity(near, engine, 331, FurnitureKindId.Shrine, "lore", new Vector(10, 0, 0), dormant: true);

        Assert.Single(near.Live);
        Assert.Empty(near.Near(96));
    }

    [Fact]
    public void Nothing_is_kept_past_the_level()
    {
        var engine = Engine(new Vector(0, 0, 0));
        var near = new FurnitureNear(engine);
        Entity(near, engine, 341, FurnitureKindId.Shrine, "lore", new Vector(10, 0, 0));

        ((IClientLevelHook)near).OnLevelShutdown();

        Assert.Empty(near.Live);
        Assert.Empty(near.Near(96));
    }

    [Fact]
    public void A_broken_piece_on_the_client_is_DEBRIS_as_TFs_are()
    {
        // TF_CLIENT_DLL's arm of BreakModelCreateSingle, after Initialize's PUSHAWAY.
        IBreakPieceGroupRule rule = new TFBreakPieceGroup();

        Assert.Equal(ClientCollisionGroup.Debris, rule.Group(ClientCollisionGroup.PushAway));
        Assert.Contains(typeof(TFBreakPieceGroup).GetCustomAttributes<ClientSideServiceAttribute>(), a => a.Side == Side.Client);
    }
}
