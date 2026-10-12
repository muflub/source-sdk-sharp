using System;

namespace SourceSharp.Abi;

/// <summary>
/// ONE ENTITY, RESOLVED ONCE, FOR THE DURATION OF ONE CROSSING
/// (plan_entity.md §3.4).
///
/// <para>
/// §3.4: "Resolution happens once per crossing. The bootstrap thunk resolves
/// the handle against the directory into a frame-scoped <c>ref struct
/// EntityRef</c> that exposes the entity object and its blob
/// <c>Span&lt;byte&gt;</c>; the compiler forbids it escaping the frame."
/// </para>
///
/// <para>
/// <b>What it replaces.</b> Every one of the thirteen entry points in
/// <see cref="ScriptedEntityRegistry"/> opened with its own
/// <c>Find(handle)</c> — a dictionary lookup per callback, for a callback that
/// then usually did one virtual call. The lookup now happens once, in
/// <see cref="ScriptedEntityRegistry.Resolve"/>, and what the switch hands the
/// instance methods is this.
/// </para>
///
/// <para>
/// <b>A <c>ref struct</c>, and that is load-bearing.</b> <see cref="Blob"/> is
/// a span over memory the SHELL owns — the entity's own allocation, valid for
/// exactly as long as the native call that produced it. A class that stored one
/// would be holding a pointer into an entity that may since have been freed,
/// and the failure would be a corrupted read on some later frame with nothing
/// naming the cause. <c>ref struct</c> makes storing it a COMPILE error, which
/// is the only form of that check anybody can rely on.
/// </para>
///
/// <para>
/// <b>The dictionary is still a dictionary.</b> §3.4 goes on: "the per-callback
/// dictionary lookup the registries do today becomes an array index, because
/// the directory is an array indexed by handle entry." That array is
/// <c>EntityDirectory</c>, which step 1g builds. Until then
/// <see cref="ScriptedEntityRegistry.Resolve"/> is one dictionary lookup rather
/// than the thirteen it replaced, and the shape this type gives the call sites
/// is what lets 1g change the lookup without touching any of them.
/// </para>
/// </summary>
public readonly ref struct EntityRef
{
    /// <summary>Builds one. Only <see cref="ScriptedEntityRegistry"/> should.</summary>
    internal EntityRef(EntityHandle handle, ScriptedEntity? entity, Span<byte> blob)
    {
        Handle = handle;
        Entity = entity;
        Blob = blob;
    }

    /// <summary>The handle the op was raised for. Valid even when nothing is bound.</summary>
    public EntityHandle Handle { get; }

    /// <summary>
    /// The managed instance driving it, or null when no class has claimed this
    /// handle. Null is ORDINARY: a shell may exist for an entity whose managed
    /// side was never created, and every op answers "nothing happened" for one.
    /// </summary>
    public ScriptedEntity? Entity { get; }

    /// <summary>
    /// The entity's payload blob, engine block first (see
    /// <see cref="EngineBlock"/>). Empty when the caller had none to give —
    /// which is what every managed-side test and every op raised outside a
    /// native call looks like, and is a legitimate answer rather than a fault.
    /// </summary>
    public Span<byte> Blob { get; }

    /// <summary>Whether an instance is bound. The guard every op opens with.</summary>
    public bool IsValid => Entity is not null;
}
