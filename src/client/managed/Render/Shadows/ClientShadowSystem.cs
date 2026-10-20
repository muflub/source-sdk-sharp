//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Diagnostics;
using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render.Shadows;

/// <summary>
/// The managed shadow manager's callers (C7 cluster 12 S2 of
/// docs/plans/c7_shadow_manager.md): native's (kClientSystems[]'s
/// CClientShadowMgr row, the plan's shadow ops, the restore callback and the
/// shells' queue, <see cref="IClientShadowsHost"/>) and managed's own (the
/// flashlight, shadow_control, the leaf registry's receivers, the material
/// proxies, the console commands). Every call goes into the manager and, when
/// the outermost one returns, the engine calls it queued are carried out
/// (<see cref="IShadowNativeQueue.Flush"/>): a question the manager asks on
/// the way has already sent what was queued before it, so the engine sees the
/// calls in stock's order.
///
/// <para>
/// THE ENTITY'S HALF IS HERE TOO. Stock kept an entity's client shadow
/// handle and its "dirty" bit on the entity (C_BaseEntity::m_ShadowHandle,
/// EFL_DIRTY_SHADOWUPDATE), and C_StandaloneEntity did the same until S2:
/// <see cref="EntityShadows"/> holds both, by entity and by renderable, and
/// answers the manager's three renderable questions about them; the rest of
/// what the manager asks a renderable goes to native.
/// </para>
/// </summary>
public sealed class ClientShadowSystem : IClientShadowsHost, IShadowProxySource, IShadowReceivers, IShadowWorldControl,
                                         IFlashlightShadows
{
    private readonly ShadowManager _manager;
    private readonly IShadowNativeQueue _queue;
    private readonly EntityShadows _entities;
    private readonly ShadowSystemStats _stats;
    private int _depth;

    public ClientShadowSystem(ShadowManager manager, IShadowNativeQueue queue, EntityShadows entities, ShadowSystemStats stats)
    {
        _manager = manager;
        _queue = queue;
        _entities = entities;
        _stats = stats;
    }

    public ShadowManager Manager => _manager;

    public EntityShadows Entities => _entities;

    public ShadowSystemStats Stats => _stats;

    // One call into the manager: the queue is sent when the outermost returns.
    private readonly struct Scope : IDisposable
    {
        private readonly ClientShadowSystem _system;
        private readonly ShadowSystemStats.Kind _kind;
        private readonly long _start;

        public Scope(ClientShadowSystem system, ShadowSystemStats.Kind kind)
        {
            _system = system;
            _kind = kind;
            _start = Stopwatch.GetTimestamp();
            system._depth++;
        }

        public void Dispose()
        {
            if (--_system._depth == 0)
            {
                _system._queue.Flush();
                _system._stats.Add(_kind, _start);
            }
        }
    }

    private Scope Enter(ShadowSystemStats.Kind kind) => new(this, kind);

    //-------------------------------------------------------------------------
    // IClientShadowsHost: native's calls.
    //-------------------------------------------------------------------------

    public bool Init()
    {
        using var _ = Enter(ShadowSystemStats.Kind.Other);
        return _manager.Init();
    }

    public void Shutdown()
    {
        using var _ = Enter(ShadowSystemStats.Kind.Other);
        _manager.Shutdown();
        _entities.Clear();
    }

    public void LevelInitPreEntity()
    {
        using var _ = Enter(ShadowSystemStats.Kind.Other);
        _manager.LevelInitPreEntity();
    }

    public void LevelShutdownPostEntity()
    {
        using var _ = Enter(ShadowSystemStats.Kind.Other);
        _manager.LevelShutdownPostEntity();
        _entities.Clear();
    }

    public void PreRender(ReadOnlySpan<int> pending)
    {
        using var _ = Enter(ShadowSystemStats.Kind.PreRender);
        _entities.Apply(pending, _manager, _stats);
        _manager.PreRender();
    }

    public void AdvanceFrame(ReadOnlySpan<int> pending)
    {
        using var _ = Enter(ShadowSystemStats.Kind.Other);
        _stats.Frame();
        _entities.Apply(pending, _manager, _stats);
        _manager.AdvanceFrame();
    }

    public void ComputeShadowTextures(ReadOnlySpan<int> leaves, ReadOnlySpan<int> pending)
    {
        using var _ = Enter(ShadowSystemStats.Kind.ShadowTextures);
        _entities.Apply(pending, _manager, _stats);
        _manager.ComputeShadowTextures(leaves);
    }

    public void ComputeShadowDepthTextures(in ShadowViewSetup view, ReadOnlySpan<int> pending)
    {
        using var _ = Enter(ShadowSystemStats.Kind.DepthTextures);
        _entities.Apply(pending, _manager, _stats);
        _manager.ComputeShadowDepthTextures(view);
    }

    public void UnlockAllShadowDepthTextures(ReadOnlySpan<int> pending)
    {
        using var _ = Enter(ShadowSystemStats.Kind.DepthTextures);
        _entities.Apply(pending, _manager, _stats);
        _manager.UnlockAllShadowDepthTextures();
    }

    public void RestoreRenderState()
    {
        using var _ = Enter(ShadowSystemStats.Kind.Other);
        _manager.RestoreRenderState();
    }

    public void RenderShadowTexture(int width, int height)
    {
        using var _ = Enter(ShadowSystemStats.Kind.Other);
        _manager.RenderShadowTexture(width, height);
    }

    public void Pending(ReadOnlySpan<int> entries)
    {
        using var _ = Enter(ShadowSystemStats.Kind.Queued);
        _entities.Apply(entries, _manager, _stats);
    }

    //-------------------------------------------------------------------------
    // Managed callers.
    //-------------------------------------------------------------------------

    /// <inheritdoc/>
    public int AtlasPage => _manager.AtlasPage;

    /// <inheritdoc/>
    public bool TryGetShadowInfo(int clientShadowHandle, out ShadowEngineInfo info)
    {
        using var _ = Enter(ShadowSystemStats.Kind.Proxies);
        return _manager.TryGetShadowInfo(clientShadowHandle, out info);
    }

    /// <inheritdoc/>
    public void AddShadowToReceiver(int shadow, nint renderable, int kind)
    {
        using var _ = Enter(ShadowSystemStats.Kind.Receivers);
        _manager.AddShadowToReceiver(shadow, renderable, kind);
    }

    /// <inheritdoc/>
    public void RemoveAllShadowsFromReceiver(nint renderable, int kind)
    {
        using var _ = Enter(ShadowSystemStats.Kind.Receivers);
        _manager.RemoveAllShadowsFromReceiver(renderable, kind);
    }

    /// <inheritdoc/>
    public void SetWorldShadows(in Vector direction, byte r, byte g, byte b, float distance, bool disabled)
    {
        using var _ = Enter(ShadowSystemStats.Kind.Other);
        _manager.SetShadowDirection(direction);
        _manager.SetShadowColor(r, g, b);
        _manager.SetShadowDistance(distance);
        _manager.SetShadowsDisabled(disabled);
    }

    /// <inheritdoc/>
    public int CreateFlashlight(in FlashlightState state)
    {
        using var _ = Enter(ShadowSystemStats.Kind.Flashlight);
        return _manager.CreateFlashlight(state);
    }

    /// <inheritdoc/>
    public void UpdateFlashlightState(int handle, in FlashlightState state)
    {
        using var _ = Enter(ShadowSystemStats.Kind.Flashlight);
        if (handle >= 0 && _manager.IsShadow(handle))
            _manager.UpdateFlashlightState(handle, state);
    }

    /// <inheritdoc/>
    public void ProjectFlashlight(int handle)
    {
        using var _ = Enter(ShadowSystemStats.Kind.Flashlight);
        if (handle >= 0 && _manager.IsShadow(handle))
            _manager.UpdateProjectedTexture(handle, true);
    }

    /// <inheritdoc/>
    public void DestroyFlashlight(int handle)
    {
        using var _ = Enter(ShadowSystemStats.Kind.Flashlight);
        if (handle >= 0 && _manager.IsShadow(handle))
            _manager.DestroyFlashlight(handle);
    }

    /// <summary>A console command's body, in a scope of its own.</summary>
    public void Command(Action<ShadowManager> body)
    {
        using var _ = Enter(ShadowSystemStats.Kind.Other);
        body(_manager);
    }
}

/// <summary>The shadow manager's receiver calls the leaf registry makes (stock's leaf system called IClientShadowMgr's).</summary>
public interface IShadowReceivers
{
    /// <summary><c>AddShadowToReceiver( shadow, renderable, kind )</c>: shadow is the client shadow handle (the leaf shadow's user id).</summary>
    void AddShadowToReceiver(int shadow, nint renderable, int kind);

    /// <summary><c>RemoveAllShadowsFromReceiver( renderable, kind )</c>.</summary>
    void RemoveAllShadowsFromReceiver(nint renderable, int kind);
}

/// <summary>Whether the shadow manager holds a shadow for an entity (by its handle's raw bits).</summary>
public interface IEntityShadowState
{
    bool HasShadow(uint entity);
}

/// <summary>shadow_control's four (stock's C_ShadowControl::OnDataChanged order).</summary>
public interface IShadowWorldControl
{
    void SetWorldShadows(in Vector direction, byte r, byte g, byte b, float distance, bool disabled);
}

/// <summary>The flashlight's projected texture, as the flashlight hands it to the shadow manager.</summary>
public interface IFlashlightShadows
{
    /// <summary>CreateFlashlight; the client shadow handle.</summary>
    int CreateFlashlight(in FlashlightState state);

    /// <summary>UpdateFlashlightState.</summary>
    void UpdateFlashlightState(int handle, in FlashlightState state);

    /// <summary>UpdateProjectedTexture( handle, true ).</summary>
    void ProjectFlashlight(int handle);

    /// <summary>DestroyFlashlight.</summary>
    void DestroyFlashlight(int handle);
}

/// <summary>
/// An entity's client shadow, held for it (stock's C_BaseEntity::m_ShadowHandle
/// and EFL_DIRTY_SHADOWUPDATE, C_StandaloneEntity's members until S2): which
/// shadow each entity has and through which renderable, and whether that
/// renderable is on the manager's dirty list. It answers the manager's
/// ShadowHandle, IsShadowDirty and MarkShadowDirty of a renderable itself;
/// every other renderable question goes to native.
///
/// <para>
/// THE QUEUE IS APPLIED HERE (shell_shadows_client.h's kinds): a dirty entry
/// is AddToDirtyShadowList( renderable ) and, with its texture bit,
/// MarkRenderToTextureShadowDirty; a refresh is C_BaseEntity::CreateShadow's
/// three arms (none: destroyed; one held: kept; else created with the flags
/// the policy wrote); a destroy is C_BaseEntity::DestroyShadow.
/// </para>
/// </summary>
public sealed class EntityShadows : IShadowRenderables, IEntityShadowState, IReloadParticipant
{
    private const int Invalid = ShadowFlags.InvalidHandle;
    public const int PendingDirty = 1;
    public const int PendingRefresh = 2;
    public const int PendingDestroy = 3;

    private readonly IShadowRenderables _native;
    private readonly Dictionary<uint, (nint Renderable, int Shadow)> _byEntity = [];
    private readonly Dictionary<nint, (int Shadow, bool Dirty)> _byRenderable = [];

    public EntityShadows(IShadowRenderables native)
    {
        _native = native;
    }

    /// <summary>The entities holding a shadow.</summary>
    public int Count => _byEntity.Count;

    /// <summary>Whether the manager holds a shadow for this entity (the dev API's "has a shadow").</summary>
    public bool HasShadow(uint entity) => _byEntity.ContainsKey(entity);

    /// <summary>Forgotten: the manager's shadows went with the level or the manager.</summary>
    public void Clear()
    {
        _byEntity.Clear();
        _byRenderable.Clear();
    }

    /// <summary>The queue's entries, in order.</summary>
    public void Apply(ReadOnlySpan<int> entries, ShadowManager manager, ShadowSystemStats? stats = null)
    {
        for (int i = 0; i + 2 < entries.Length; i += 3)
        {
            uint entity = unchecked((uint)entries[i + 1]);
            int value = entries[i + 2];
            stats?.CountPending(entries[i]);
            switch (entries[i])
            {
                case PendingDirty: Dirty(manager, entity, value != 0); break;
                case PendingRefresh: Refresh(manager, entity, value & 0xFF, (value >> 8) & 0xFFFF); break;
                case PendingDestroy: Destroy(manager, entity); break;
            }
        }
    }

    private void Dirty(ShadowManager manager, uint entity, bool textureDirty)
    {
        if (!_byEntity.TryGetValue(entity, out var held) || !Live(manager, entity, held.Shadow))
            return;
        manager.AddToDirtyShadowList(held.Renderable, false);
        if (textureDirty)
            manager.MarkRenderToTextureShadowDirty(held.Shadow);
    }

    private void Refresh(ShadowManager manager, uint entity, int shadowType, int shadowFlags)
    {
        if (shadowType == ShadowTypes.None)
        {
            Destroy(manager, entity);
            return;
        }

        if (_byEntity.TryGetValue(entity, out var held))
        {
            // The manager recomputes the kind itself from the flags it was
            // given; an entity that already has a shadow keeps it.
            if (Live(manager, entity, held.Shadow))
                return;
            Forget(entity);
        }

        nint renderable = _native.RenderableOf(entity);
        if (renderable == 0)
            return;

        // CreateShadow marks the renderable dirty before the entity has its
        // handle ("this entity hasn't set its shadow handle yet").
        _byRenderable[renderable] = (Invalid, false);
        int shadow = manager.CreateShadow(entity, shadowFlags);
        _byEntity[entity] = (renderable, shadow);
        _byRenderable[renderable] = (shadow, _byRenderable[renderable].Dirty);
    }

    private void Destroy(ShadowManager manager, uint entity)
    {
        if (!_byEntity.TryGetValue(entity, out var held))
            return;
        if (Live(manager, entity, held.Shadow))
            manager.DestroyShadow(held.Shadow);
        Forget(entity);
    }

    // The handle still names this entity's shadow (the manager retires a shadow
    // whose renderable is gone on its own, and may hand the handle out again).
    private static bool Live(ShadowManager manager, uint entity, int shadow)
        => shadow != Invalid && manager.IsShadow(shadow) && manager.EntityOf(shadow) == entity;

    private void Forget(uint entity)
    {
        if (_byEntity.Remove(entity, out var held))
            _byRenderable.Remove(held.Renderable);
    }

    //-------------------------------------------------------------------------
    // IShadowRenderables: the three answers held here, the rest native's.
    //-------------------------------------------------------------------------

    public int ShadowHandle(nint renderable) => _byRenderable.TryGetValue(renderable, out var s) ? s.Shadow : Invalid;

    public bool IsShadowDirty(nint renderable) => _byRenderable.TryGetValue(renderable, out var s) && s.Dirty;

    public void MarkShadowDirty(nint renderable, bool dirty)
    {
        if (_byRenderable.TryGetValue(renderable, out var s))
            _byRenderable[renderable] = (s.Shadow, dirty);
    }

    public nint RenderableOf(uint entity) => _native.RenderableOf(entity);

    public Vector RenderOrigin(nint renderable) => _native.RenderOrigin(renderable);

    public QAngle RenderAngles(nint renderable) => _native.RenderAngles(renderable);

    public void RenderableToWorldTransform(nint renderable, out ShadowMatrix3x4 transform)
        => _native.RenderableToWorldTransform(renderable, out transform);

    public nint Model(nint renderable) => _native.Model(renderable);

    public int ModelType(nint model) => _native.ModelType(model);

    public int Body(nint renderable) => _native.Body(renderable);

    public int Skin(nint renderable) => _native.Skin(renderable);

    public void RenderBoundsWorldspace(nint renderable, out Vector mins, out Vector maxs)
        => _native.RenderBoundsWorldspace(renderable, out mins, out maxs);

    public void ShadowRenderBounds(nint renderable, int shadowType, out Vector mins, out Vector maxs)
        => _native.ShadowRenderBounds(renderable, shadowType, out mins, out maxs);

    public bool ShouldReceiveProjectedTextures(nint renderable, int flags) => _native.ShouldReceiveProjectedTextures(renderable, flags);

    public bool ShadowCastDistance(nint renderable, int shadowType, ref float distance)
        => _native.ShadowCastDistance(renderable, shadowType, ref distance);

    public bool ShadowCastDirection(nint renderable, int shadowType, ref Vector direction)
        => _native.ShadowCastDirection(renderable, shadowType, ref direction);

    public nint ShadowParent(nint renderable) => _native.ShadowParent(renderable);

    public nint FirstShadowChild(nint renderable) => _native.FirstShadowChild(renderable);

    public nint NextShadowPeer(nint renderable) => _native.NextShadowPeer(renderable);

    public void CreateModelInstance(nint renderable) => _native.CreateModelInstance(renderable);

    public int ModelInstance(nint renderable) => _native.ModelInstance(renderable);

    public int NetworkableEntIndex(nint renderable) => _native.NetworkableEntIndex(renderable);

    public nint FirstMoveChild(nint renderable) => _native.FirstMoveChild(renderable);

    public nint NextMovePeer(nint renderable) => _native.NextMovePeer(renderable);

    //-------------------------------------------------------------------------
    // Hot reload: the entities' half outlives the module as the manager's does.
    //-------------------------------------------------------------------------

    public void Save(ReloadWriter writer)
    {
        writer.Write(_byEntity.Count);
        foreach (var (entity, held) in _byEntity)
        {
            writer.Write(unchecked((int)entity));
            long p = held.Renderable;
            writer.Write(unchecked((int)p));
            writer.Write(unchecked((int)(p >> 32)));
            writer.Write(held.Shadow);
            writer.Write(_byRenderable.TryGetValue(held.Renderable, out var s) && s.Dirty);
        }
    }

    public void Restore(ReloadReader reader)
    {
        Clear();
        int n = reader.ReadInt();
        for (int i = 0; i < n; i++)
        {
            uint entity = unchecked((uint)reader.ReadInt());
            uint lo = unchecked((uint)reader.ReadInt());
            uint hi = unchecked((uint)reader.ReadInt());
            nint renderable = (nint)(((long)hi << 32) | lo);
            int shadow = reader.ReadInt();
            bool dirty = reader.ReadBool();
            _byEntity[entity] = (renderable, shadow);
            _byRenderable[renderable] = (shadow, dirty);
        }
    }
}

/// <summary>
/// The shadow manager's cost (S3's instrument, cs_cl_shadow_stats): its calls
/// by kind with the managed time each took (the native runs inside included),
/// the queue's entries, and the runs it sent native
/// (<see cref="ShadowCallCounts"/>), per frame. PreRender runs once or twice a
/// frame, the plan's AdvanceFrame once, so AdvanceFrame's count is the frames.
/// </summary>
public sealed class ShadowSystemStats
{
    public enum Kind
    {
        PreRender,
        ShadowTextures,
        DepthTextures,
        Queued,
        Receivers,
        Flashlight,
        Proxies,
        Other,
    }

    private readonly long[] _calls = new long[Enum.GetValues<Kind>().Length];
    private readonly long[] _ticks = new long[Enum.GetValues<Kind>().Length];
    private readonly long[] _pending = new long[4];
    private long _frames;

    /// <summary>The native runs, when the native side is the forwarder library's.</summary>
    public ShadowCallCounts? Outbound { get; set; }

    public long Calls(Kind kind) => _calls[(int)kind];

    public double Milliseconds(Kind kind) => _ticks[(int)kind] * 1000.0 / Stopwatch.Frequency;

    public long Pending(int kind) => kind is >= 1 and <= 3 ? _pending[kind] : 0;

    public long Frames => _frames;

    public void Add(Kind kind, long startTicks)
    {
        _calls[(int)kind]++;
        _ticks[(int)kind] += Stopwatch.GetTimestamp() - startTicks;
    }

    public void CountPending(int kind)
    {
        if (kind is >= 1 and <= 3)
            _pending[kind]++;
    }

    public void Frame() => _frames++;

    public void Reset()
    {
        Array.Clear(_calls);
        Array.Clear(_ticks);
        Array.Clear(_pending);
        _frames = 0;
        Outbound?.Reset();
    }

    public string Report()
    {
        long frames = Math.Max(1, _frames);
        var sb = new StringBuilder();
        var c = CultureInfo.InvariantCulture;
        sb.Append(c, $"[SourceSharp/Client] shadow manager: {_frames} frame(s) (AdvanceFrame calls)\n");
        double totalMs = 0;
        foreach (Kind kind in Enum.GetValues<Kind>())
        {
            totalMs += Milliseconds(kind);
            sb.Append(c, $"[SourceSharp/Client]   in   {kind,-18} {Calls(kind),9} calls  {(double)Calls(kind) / frames,9:F2}/frame  {Milliseconds(kind) / frames * 1000.0,9:F1} us/frame\n");
        }
        sb.Append(c, $"[SourceSharp/Client]   in   total managed           {totalMs / frames * 1000.0,9:F1} us/frame\n");
        sb.Append(c, $"[SourceSharp/Client]   queue dirty {Pending(1)}  refresh {Pending(2)}  destroy {Pending(3)}  ({(double)(Pending(1) + Pending(2) + Pending(3)) / frames:F2}/frame)\n");
        if (Outbound is { } outbound)
        {
            sb.Append(c, $"[SourceSharp/Client]   out  runs {outbound.Runs,9}  {(double)outbound.Runs / frames,9:F2}/frame  records {(double)outbound.Records / frames,9:F1}/frame  " +
                         $"questions {(double)outbound.Questions / frames,9:F1}/frame  records/run {(outbound.Runs == 0 ? 0 : (double)outbound.Records / outbound.Runs),6:F2}  largest {outbound.LargestRun}\n");
        }
        return sb.ToString();
    }
}
