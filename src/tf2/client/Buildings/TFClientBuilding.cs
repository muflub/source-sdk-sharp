//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Standalone;

using TF2.Shared.Buildings;
using TF2.Shared.Player;

namespace TF2.Client.Buildings;

/// <summary>
/// The client mirror of stock's <c>C_BaseObject</c> (game/client/tf/c_baseobject.cpp):
/// an empty shell per building classname, built by the registry when the
/// server's building entity arrives. Its first job is to be the type the
/// material proxies' cast asks for — stock's <c>dynamic_cast&lt;C_BaseObject*&gt;</c>
/// in <c>CObjectPowerProxy::OnBind</c> (c_baseobject.cpp:1172) and
/// <c>IsBaseObject()</c> in <c>CBuildingInvisProxy::OnBind</c> (:1228).
///
/// <para>
/// It carries no networked state: the building fields a proxy would read have
/// no writers in stock's drop either — <c>m_flInvisibilityPercent</c> is set by
/// nobody and rides no recv table (c_baseobject.cpp:1089-1111) — so the level
/// the fade answers is constant zero (docs/plans/tf2/deferred.tsv).
/// </para>
/// </summary>
public abstract class TFClientBuilding(IEngineEntityRender render) : ClientStandaloneEntity(render)
{
    /// <summary>The build state the server publishes; the sentry's shadow arm reads <see cref="TFObjectNetState.Placing"/>.</summary>
    [NetState] public TFObjectNetState State = new();
}

/// <summary>
/// Stock's <c>C_ObjectSentrygun</c> client half. Its shadow is the one place
/// the build state shows: while the blueprint is worn the shadow is the
/// collision hull widened by <c>(10, 10, 0)</c>
/// (<c>C_ObjectSentrygun::GetShadowRenderBounds</c>, c_obj_sentrygun.cpp:588),
/// not the model's own render bounds — the blueprint's are the map-sized box
/// its art was authored in, and a shadow cast from them darkens the floor
/// half a map away.
/// </summary>
[ClientStandaloneEntity(TFEntityNames.SentryGun)]
public sealed class ClientTFSentryGun(IEngineEntityRender render) : TFClientBuilding(render)
{
    public override string ClassName => TFEntityNames.SentryGun;

    private bool? _shadowHullShown;

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        if (_shadowHullShown != State.Placing)
        {
            _shadowHullShown = State.Placing;
            Render.SetShadowHull(Handle, State.Placing);
        }
    }
}

/// <summary>Stock's <c>C_BaseObject</c> mirror for <c>obj_dispenser</c>.</summary>
[ClientStandaloneEntity(TFEntityNames.Dispenser)]
public sealed class ClientTFDispenser(IEngineEntityRender render) : TFClientBuilding(render)
{
    public override string ClassName => TFEntityNames.Dispenser;
}

/// <summary>Stock's <c>C_BaseObject</c> mirror for <c>obj_teleporter</c>.</summary>
[ClientStandaloneEntity(TFEntityNames.Teleporter)]
public sealed class ClientTFTeleporter(IEngineEntityRender render) : TFClientBuilding(render)
{
    public override string ClassName => TFEntityNames.Teleporter;
}
