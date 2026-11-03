//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Standalone;

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
public abstract class TFClientBuilding(IEngineEntityRender render) : ClientStandaloneEntity(render);

/// <summary>Stock's <c>C_BaseObject</c> mirror for <c>obj_sentrygun</c>.</summary>
[ClientStandaloneEntity(TFEntityNames.SentryGun)]
public sealed class ClientTFSentryGun(IEngineEntityRender render) : TFClientBuilding(render)
{
    public override string ClassName => TFEntityNames.SentryGun;
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
