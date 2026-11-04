//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render;

/// <summary>
/// The glow-object registry and its outline pass, stock's
/// <c>CGlowObjectManager</c> (glow_outline_effect.h/.cpp): the slot list with
/// its free-list head, the <c>ShouldDraw</c> gate (an entity, its slot, one of
/// the render flags, not dormant), and the three halves of
/// <c>ApplyEntityGlowEffects</c> — the stencil marks with blending off, the
/// colour-times-alpha blobs into <c>_rt_FullFrameFB</c>, and the quarter-size
/// halo blit that tests the marked pixels out. Nothing TF-specific lives here;
/// callers register their entities and colours through the seams below.
///
/// <para>
/// Stock's entity-side halves reach the engine through C++ pointers; here a
/// registered entity draws when its entry is in the frame's scene list and the
/// <paramref name="isDormant"/> seam answers false, the attachment walk
/// (<c>FirstMoveChild</c>/<c>NextMovePeer</c>, glow_outline_effect.cpp:313-329)
/// is not ported, and the blobs keep their own materials with the colour
/// modulation carrying the glow where stock forces <c>dev/glow_color</c> —
/// docs/divergences.md holds the rows.
/// </para>
/// </summary>
public sealed class GlowObjectManager
{
    /// <summary>The free list's tail, stock's <c>END_OF_FREE_LIST</c>.</summary>
    public const int EndOfFreeList = -1;

    /// <summary>A taken slot, stock's <c>ENTRY_IN_USE</c>.</summary>
    public const int EntryInUse = -2;

    /// <summary>The slot a glow wants in every split-screen view, stock's <c>GLOW_FOR_ALL_SPLIT_SCREEN_SLOTS</c>.</summary>
    public const int GlowForAllSplitScreenSlots = -1;

    private struct Definition
    {
        public EntityHandle Entity;
        public Vector Color;
        public float Alpha;
        public bool RenderOccluded;
        public bool RenderUnoccluded;
        public int SplitScreenSlot;
        public int NextFreeSlot;
    }

    private readonly List<Definition> _definitions = [];
    private readonly List<int> _drawables = [];
    private int _firstFreeSlot = EndOfFreeList;
    private int _haloId = -1;
    private readonly Func<EntityHandle, bool> _isDormant;
    private readonly GlowOutlineEffectEnableConVar _enable;
    private readonly Func<string, int> _findMaterial;

    /// <summary>Stock's three engine reads: the entity's dormancy, the glow convar, the halo material.</summary>
    public GlowObjectManager(Func<EntityHandle, bool> isDormant, GlowOutlineEffectEnableConVar enable, Func<string, int> findMaterial)
    {
        _isDormant = isDormant;
        _enable = enable;
        _findMaterial = findMaterial;
    }

    /// <summary>The slots in the array, in-use and free alike (<c>m_GlowObjectDefinitions.Count()</c>).</summary>
    public int Count => _definitions.Count;

    /// <summary>An entity's glow: colour and alpha, when it renders, which split-screen slot's views show it. Returns its slot.</summary>
    public int RegisterGlowObject(EntityHandle entity, Vector color, float alpha, bool renderOccluded, bool renderUnoccluded,
                                  int splitScreenSlot = GlowForAllSplitScreenSlots)
    {
        var def = new Definition
        {
            Entity = entity, Color = color, Alpha = alpha, RenderOccluded = renderOccluded, RenderUnoccluded = renderUnoccluded, NextFreeSlot = EntryInUse,
        };
        def.SplitScreenSlot = splitScreenSlot;

        if (_firstFreeSlot == EndOfFreeList)
        {
            _definitions.Add(def);
            return _definitions.Count - 1;
        }

        int slot = _firstFreeSlot;
        _firstFreeSlot = _definitions[slot].NextFreeSlot;
        _definitions[slot] = def;
        return slot;
    }

    /// <summary>Gives the slot back: the entity clears and the slot joins the free list's head.</summary>
    public void UnregisterGlowObject(int handle)
    {
        if (_definitions[handle].NextFreeSlot != EntryInUse)
            return;

        var def = _definitions[handle];
        def.NextFreeSlot = _firstFreeSlot;
        def.Entity = default;
        _definitions[handle] = def;
        _firstFreeSlot = handle;
    }

    /// <summary>Which entity a slot answers for, and which not (stock's <c>SetEntity</c>).</summary>
    public void SetEntity(int handle, EntityHandle entity)
    {
        var def = _definitions[handle];
        def.Entity = entity;
        _definitions[handle] = def;
    }

    /// <summary>Stock's <c>SetColor</c>.</summary>
    public void SetColor(int handle, Vector color)
    {
        var def = _definitions[handle];
        def.Color = color;
        _definitions[handle] = def;
    }

    /// <summary>Stock's <c>SetAlpha</c>.</summary>
    public void SetAlpha(int handle, float alpha)
    {
        var def = _definitions[handle];
        def.Alpha = alpha;
        _definitions[handle] = def;
    }

    /// <summary>Stock's <c>SetRenderFlags</c>.</summary>
    public void SetRenderFlags(int handle, bool renderOccluded, bool renderUnoccluded)
    {
        var def = _definitions[handle];
        def.RenderOccluded = renderOccluded;
        def.RenderUnoccluded = renderUnoccluded;
        _definitions[handle] = def;
    }

    /// <summary>Stock's <c>IsRenderingWhenOccluded</c>.</summary>
    public bool IsRenderingWhenOccluded(int handle) => _definitions[handle].RenderOccluded;

    /// <summary>The slot's colour (stock's <c>GlowObjectDefinition_t::m_vColor</c>, read through the manager).</summary>
    public Vector GetColor(int handle) => _definitions[handle].Color;

    /// <summary>Stock's <c>IsRenderingWhenUnoccluded</c>.</summary>
    public bool IsRenderingWhenUnoccluded(int handle) => _definitions[handle].RenderUnoccluded;

    /// <summary>Whether anything answers for this entity already (stock's <c>HasGlowEffect</c>, which the attach walk asks).</summary>
    public bool HasGlowEffect(EntityHandle entity)
    {
        foreach (Definition def in _definitions)
            if (def.NextFreeSlot == EntryInUse && def.Entity == entity)
                return true;
        return false;
    }

    // GlowObjectDefinition_t::ShouldDraw: the entity, its slot, one render flag,
    // and the engine reads the port answers through the seams (its entry in the
    // frame's list stands in for ShouldDraw, the injected answer for IsDormant).
    private bool ShouldDraw(in Definition def, int slot)
        => def.NextFreeSlot == EntryInUse
           && def.Entity != EntityHandle.None
           && (def.SplitScreenSlot == GlowForAllSplitScreenSlots || def.SplitScreenSlot == slot)
           && (def.RenderOccluded || def.RenderUnoccluded)
           && !_isDormant(def.Entity);

    private int FindEntry(ViewSceneData scene, EntityHandle entity)
    {
        var entries = scene.Entries;
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].EntIndex == entity.EntryIndex && entries[i].Has(ViewSceneEntryFlags.Renderable))
                return i;
        return -1;
    }

    /// <summary>
    /// The frame's glow: the mark pass, the blobs into the full-frame target,
    /// and the halo composite, as stock orders them inside
    /// <c>ApplyEntityGlowEffects</c>. <paramref name="dxLevel"/> is the frame's
    /// hardware tier (stock's <c>SupportsPixelShaders_2_0</c> half answers 80
    /// here, the plan's other DX gate too); <paramref name="splitScreenSlot"/>
    /// the view's slot; the rect the view's pixels. An entity with no entry in
    /// the scene list draws no model but still counts for the composite — the
    /// list is culled, stock's DrawModel was not.
    /// </summary>
    public void RenderGlowEffects(ViewSceneData scene, int width, int height, int dxLevel, int splitScreenSlot, RenderPlanBuilder b)
    {
        if (dxLevel < 80)
            return;
        if (!_enable.Value)
            return;

        _drawables.Clear();
        for (int slot = 0; slot < _definitions.Count; slot++)
            if (ShouldDraw(_definitions[slot], splitScreenSlot))
                _drawables.Add(slot);
        if (_drawables.Count == 0)
            return;

        // The mark pass, stock's two loops: blending off and the depth override
        // on (test against the frame's depth, no writes, glow_outline_effect.cpp
        // :170/:251), each object's silhouette into the stencil by its flags —
        // the arm stock sets per draw — then the occluded-only warm of the
        // second loop. Blend 1 after, as the saved value the sections around
        // this leave.
        b.Add(RenderOpKind.SetBlend, default, [0f]);
        b.Add(RenderOpKind.PostOverrideDepth, [1, 0]);
        bool warm = false;
        foreach (int slot in _drawables)
        {
            Definition def = _definitions[slot];
            int entry = FindEntry(scene, def.Entity);
            if (entry < 0)
                continue;

            if (def.RenderOccluded && def.RenderUnoccluded)
                b.Add(RenderOpKind.PostStencil, [4]);
            else if (def.RenderOccluded)
            {
                b.Add(RenderOpKind.PostStencil, [5]);
                warm = true;
            }
            else
                b.Add(RenderOpKind.PostStencil, [6]);
            b.Add(RenderOpKind.DrawEntity, [entry, ViewDrawProgram.StudioRender, 0]);
        }

        if (warm)
            foreach (int slot in _drawables)
            {
                Definition def = _definitions[slot];
                if (!(def.RenderOccluded && !def.RenderUnoccluded))
                    continue;
                int entry = FindEntry(scene, def.Entity);
                if (entry < 0)
                    continue;
                b.Add(RenderOpKind.PostStencil, [7]);
                b.Add(RenderOpKind.DrawEntity, [entry, ViewDrawProgram.StudioRender, 0]);
            }

        b.Add(RenderOpKind.PostOverrideDepth, [0, 0]);
        b.Add(RenderOpKind.SetBlend, default, [1f]);
        b.Add(RenderOpKind.PostStencil, [0]);

        // RenderGlowModels: the coloured blobs to _rt_FullFrameFB, cleared black.
        b.Op(RenderOpKind.PostPushRenderTargetAndViewport);
        b.Add(RenderOpKind.PostSetRenderTargetAndViewport, [(int)PostTexture.FullFrameFB]);
        b.Add(RenderOpKind.PostClearColor3ub, [0, 0, 0]);
        // Stock clears colour only (its glow runs after the frame's depth went
        // to the post chain); from inside the scene section the target's stale
        // depth blocks every fragment, so the depth goes too.
        b.Add(RenderOpKind.PostClearBuffers, [1, 1]);
        foreach (int slot in _drawables)
        {
            Definition def = _definitions[slot];
            int entry = FindEntry(scene, def.Entity);
            if (entry < 0)
                continue;

            float alpha = def.Alpha;
            b.Add(RenderOpKind.SetBlend, default, [alpha]);
            b.Add(RenderOpKind.SetColorModulation, default, [def.Color.X * alpha, def.Color.Y * alpha, def.Color.Z * alpha]);
            b.Add(RenderOpKind.DrawEntity, [entry, ViewDrawProgram.StudioRender, 0]);
        }
        b.Add(RenderOpKind.SetColorModulation, default, [1f, 1f, 1f]);
        b.Add(RenderOpKind.SetBlend, default, [1f]);
        b.Op(RenderOpKind.PopRenderTargetAndViewport);

        // The composite: the quarter-size halo added over the frame everywhere
        // the marks did not reach — the equal-vs-zero stencil of stock's
        // RenderCompositeEffect, which the executor's third arm answers.
        if (_haloId < 0)
            _haloId = _findMaterial("dev/halo_add_to_screen");
        if (_haloId == 0)
            return;

        b.Add(RenderOpKind.PostMaterialVarFloat, [_haloId, (int)PostVar.C0X], [1f]);
        b.Add(RenderOpKind.PostStencil, [3]);
        b.Add(RenderOpKind.PostDrawRect, [_haloId, 0, 0, width, height, (width + 3) / 4, (height + 3) / 4],
              [0f, -0.5f, width / 4 - 1, height / 4 - 1, 1f, 1f, 0f]);
        b.Add(RenderOpKind.PostStencil, [0]);
    }
}
