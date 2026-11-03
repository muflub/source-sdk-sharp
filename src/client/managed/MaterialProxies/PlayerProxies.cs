//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace SourceSharp.Client.MaterialProxies;

/// <summary>
/// How proxyplayer.cpp's proxies reach the player they measure against — the
/// reads stock took from <c>C_BasePlayer::GetLocalPlayer</c>, the team table
/// and the per-entity velocity, none of which the bind itself carries. The
/// engine block gives the bound entity (its origin, alpha, NPC flag); this is
/// the other side of the same bind: whose eyes the material is being drawn
/// through, and what the rest of the world's simulation knows about the
/// entities named by index.
/// </summary>
public interface IPlayerProxyEyes
{
    /// <summary><c>C_BasePlayer::GetLocalPlayer()</c>: the local player's standalone, or null before the client has one.</summary>
    ClientSsPlayer? LocalPlayer();

    /// <summary>
    /// The local view's published angles — stock read the local player's
    /// <c>GetAbsAngles()</c>, which for the player is the eye direction the
    /// prediction just published. Null with no local player.
    /// </summary>
    Vector? LocalAngles();

    /// <summary>
    /// The <c>GetTeamNumber()</c> of the entity at the index: the player
    /// resource's row for a player, 0 (<c>TEAM_UNASSIGNED</c>) for anything
    /// else — what stock's non-player <c>C_BaseEntity::GetTeamNumber</c> answers.
    /// </summary>
    int TeamNumber(int entityIndex);

    /// <summary>
    /// The bound entity's <c>GetLocalVelocity().Length()</c>: the decoded (or,
    /// for the local player, replayed) velocity for a player. Null when nothing
    /// carries one — the declared-refusal arm of <see cref="EntitySpeedProxy"/>.
    /// </summary>
    float? EntitySpeed(int entityIndex);
}

/// <summary>
/// The eyes over the client's real state, mirroring how TF's proxies reach
/// their players (TFMaterialProxies' <c>TFProxyPlayers</c>): the standalone
/// directory by bound index, the player resource's team rows, and the view
/// angles the prediction publishes.
/// </summary>
public sealed class PlayerProxyEyes(IClientStandalones standalones, IEngineHud screen, ViewReadout view)
    : IPlayerProxyEyes
{
    private const int Slots = PlayerResourceNetState.Slots;

    private ClientPlayerResource? _core;

    public ClientSsPlayer? LocalPlayer()
    {
        int local = screen.LocalPlayerIndex;
        return local > 0 ? standalones.GetByIndex(local) as ClientSsPlayer : null;
    }

    public Vector? LocalAngles() => LocalPlayer() is null ? null : new Vector(view.Pitch, view.Yaw, 0f);

    public int TeamNumber(int entityIndex)
        => standalones.GetByIndex(entityIndex) is ClientSsPlayer
           && Core is { } resource && entityIndex < Slots
            ? resource.State.Team[entityIndex]
            : 0;

    public float? EntitySpeed(int entityIndex)
        => standalones.GetByIndex(entityIndex) is ClientSsPlayer player
            ? player.State.Velocity.Length
            : null;

    private ClientPlayerResource? Core
    {
        get
        {
            if (_core is null || !standalones.Live().Contains(_core))
                _core = standalones.OfClass(EntityNames.PlayerManager)
                                   .OfType<ClientPlayerResource>().FirstOrDefault();
            return _core;
        }
    }
}

/// <summary>
/// No container, no eyes (a hand-built test's factory): nothing to measure
/// against, so every player-dependent arm stays stock's early return and each
/// material keeps the value its .vmt declared.
/// </summary>
internal sealed class NoPlayerProxyEyes : IPlayerProxyEyes
{
    public ClientSsPlayer? LocalPlayer() => null;

    public Vector? LocalAngles() => null;

    public int TeamNumber(int entityIndex) => 0;

    public float? EntitySpeed(int entityIndex) => null;
}

/// <summary>
/// <c>CPlayerProximityProxy</c> ("PlayerProximity", proxyplayer.cpp:26-68): the
/// local player's distance to the bound entity, scaled (<c>scale</c> 0.002,
/// :41) into the result.
///
/// <para>
/// The one divergence, stated: stock measures center to center
/// (<c>WorldSpaceCenter()</c>, :57). The bind seam carries the entity's origin
/// (the engine block's <c>GetAbsOrigin</c>) and the net state the local
/// player's, and no interface on this boundary carries model mins/maxs — so
/// this measures origin to origin. The distance differs from stock's by the
/// two centers' fixed offsets above their origins: materials use it as a
/// falloff curve's input, which the offset shifts on its scale but never
/// inverts.
/// </para>
/// </summary>
public sealed class PlayerProximityProxy(IPlayerProxyEyes eyes) : ResultProxy
{
    private float _factor;

    public override bool Init(IProxyInit init)
    {
        if (!base.Init(init))
            return false;
        _factor = init.Keys.GetFloat("scale", 0.002f);
        return true;
    }

    public override void Bind(IProxyBind bind)
    {
        if (bind.Arg == 0)
            return;
        ProxyEntity entity = bind.Entity();
        if (!entity.IsEntity)
            return;

        if (eyes.LocalPlayer() is not { } player)
            return;

        Vector delta = entity.Origin - player.State.Origin;
        SetFloatResult(bind, delta.Length * _factor);
    }
}

/// <summary>
/// <c>CPlayerTeamMatchProxy</c> ("PlayerTeamMatch", proxyplayer.cpp:75-112):
/// 1.0 when the bound entity's team is the local player's, 0.0 when it is not
/// (:104) — the ctf_2fort capture-banner material's switch.
/// </summary>
public sealed class PlayerTeamMatchProxy(IPlayerProxyEyes eyes) : ResultProxy
{
    public override void Bind(IProxyBind bind)
    {
        if (bind.Arg == 0)
            return;
        ProxyEntity entity = bind.Entity();
        if (!entity.IsEntity)
            return;

        if (eyes.LocalPlayer() is not { } player)
            return;

        SetFloatResult(bind, eyes.TeamNumber(entity.EntIndex) == eyes.TeamNumber(player.Index) ? 1f : 0f);
    }
}

/// <summary>
/// <c>CPlayerViewProxy</c> ("PlayerView", proxyplayer.cpp:118-164): how squarely
/// the local view looks at the bound entity — the unit delta dotted with the
/// view's forward, scaled (<c>scale</c> 2, :133).
/// </summary>
public sealed class PlayerViewProxy(IPlayerProxyEyes eyes) : ResultProxy
{
    private float _factor;

    public override bool Init(IProxyInit init)
    {
        if (!base.Init(init))
            return false;
        _factor = init.Keys.GetFloat("scale", 2f);
        return true;
    }

    public override void Bind(IProxyBind bind)
    {
        if (bind.Arg == 0)
            return;
        ProxyEntity entity = bind.Entity();
        if (!entity.IsEntity)
            return;

        if (eyes.LocalPlayer() is not { } player || eyes.LocalAngles() is not { } angles)
            return;

        Vector delta = entity.Origin - player.State.Origin;
        MoveVec.Normalize(ref delta);                                  // :150
        MoveVec.AngleVectors(angles, out Vector forward, out _, out _); // :153
        SetFloatResult(bind, forward.Dot(delta) * _factor);             // :156
    }
}

/// <summary>
/// <c>CPlayerSpeedProxy</c> ("PlayerSpeed", proxyplayer.cpp:170-205): the local
/// player's speed, scaled (<c>scale</c> 0.005, :185). Stock never touches the
/// bind argument here (:189-203), so neither does this — the proxy answers on
/// materials bound to nothing.
/// </summary>
public sealed class PlayerSpeedProxy(IPlayerProxyEyes eyes) : ResultProxy
{
    private float _factor;

    public override bool Init(IProxyInit init)
    {
        if (!base.Init(init))
            return false;
        _factor = init.Keys.GetFloat("scale", 0.005f);
        return true;
    }

    public override void Bind(IProxyBind bind)
    {
        if (eyes.LocalPlayer() is not { } player)
            return;

        SetFloatResult(bind, player.State.Velocity.Length * _factor);
    }
}

/// <summary>
/// <c>CPlayerPositionProxy</c> ("PlayerPosition", proxyplayer.cpp:211-249): the
/// local player's position scaled by <c>scale</c> (0.005, :226) into the result
/// as three floats (<c>SetVecValue(res.Base(), 3)</c>, :241 — the array setter,
/// not the xyz one). The same center-versus-origin divergence as
/// <see cref="PlayerProximityProxy"/>'s remarks states.
/// </summary>
public sealed class PlayerPositionProxy(IPlayerProxyEyes eyes) : ResultProxy
{
    private float _factor;

    public override bool Init(IProxyInit init)
    {
        if (!base.Init(init))
            return false;
        _factor = init.Keys.GetFloat("scale", 0.005f);
        return true;
    }

    public override void Bind(IProxyBind bind)
    {
        if (eyes.LocalPlayer() is not { } player)
            return;

        Vector res = player.State.Origin * _factor;
        Span<float> values = [res.X, res.Y, res.Z];
        bind.SetVec(Result, values, 3);
    }
}

/// <summary>
/// <c>CEntitySpeedProxy</c> ("EntitySpeed", proxyplayer.cpp:255-279): the bound
/// entity's own speed into the result — stock's direct
/// <c>SetFloatValue</c> (:271), not <c>SetFloatResult</c>'s vector-aware arm.
/// A player's velocity is on the net state the replay keeps current; anything
/// else has no carrier, so the ask is a declared refusal (the not-shipped
/// ledger) and the result is left as the .vmt declared it, per the
/// <see cref="HealthProxy"/> precedent.
/// </summary>
public sealed class EntitySpeedProxy(IPlayerProxyEyes eyes) : ResultProxy
{
    public const string Capability = "material proxy \"EntitySpeed\" for a non-player entity";

    public const string Reason =
        "a non-player entity's velocity is on no engine-block field and on no interface "
        + "on this boundary (only the managed standalones' net states carry one); the "
        + "material's result is left as the .vmt declared it rather than given a plausible constant";

    public override void Bind(IProxyBind bind)
    {
        if (bind.Arg == 0)
            return;
        ProxyEntity entity = bind.Entity();
        if (!entity.IsEntity)
            return;

        if (eyes.EntitySpeed(entity.EntIndex) is not { } speed)
        {
            bind.NotShipped(Capability, Reason);
            return;
        }

        bind.SetFloat(Result, speed);
    }
}

/// <summary>
/// <c>CEntityRandomProxy</c> ("EntityRandom", proxyplayer.cpp:285-324): a
/// per-entity random in 0..1 times the <c>scale</c> <c>CFloatInput</c>
/// (default 1, :300). Stock multiplies by <c>ProxyRandomValue()</c> —
/// <c>m_flProxyRandomValue</c>, drawn on entity spawn with no networked field
/// and no interface on this boundary — so the read is a declared refusal (the
/// not-shipped ledger, the <see cref="HealthProxy"/> precedent) and the result
/// is left as the .vmt declared it.
/// </summary>
public sealed class EntityRandomProxy : ResultProxy
{
    public const string Capability = "material proxy \"EntityRandom\"";

    public const string Reason =
        "the entity's m_flProxyRandomValue is drawn on spawn, is not a networked field, "
        + "and is on no interface on this boundary; the material's result is left as the "
        + ".vmt declared it rather than given a plausible constant";

    private ProxyFloatInput _factor;

    public override bool Init(IProxyInit init)
    {
        if (!base.Init(init))
            return false;
        return _factor.Init(init, "scale", 1);
    }

    public override void Bind(IProxyBind bind)
    {
        if (bind.Arg == 0)
            return;
        if (!bind.Entity().IsEntity)
            return;

        bind.NotShipped(Capability, Reason);
    }
}
