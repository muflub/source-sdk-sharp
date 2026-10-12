//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render;

/// <summary>
/// One world view's draws over its lists (plan_client_novalve C7 step 2):
/// the decisions stock's CRendering3dView made per renderable and per leaf
/// (viewrender.cpp's DrawOpaqueRenderables, DrawTranslucentRenderables,
/// DrawTranslucentRenderablesNoWorld, DrawNoZBufferTranslucentRenderables,
/// DrawTranslucentWorldInLeaves and DrawTranslucentWorldAndDetailPropsInLeaves),
/// made from the view's <see cref="ViewSceneData"/> and emitted as ops.
///
/// <para>
/// The plan's <see cref="RenderOpKind.ViewScene"/> op is where native hands the
/// scene over; the program has one section (ended by
/// <see cref="RenderOpKind.SectionEnd"/>) per list-dependent draw op of that view,
/// in order, and each of those ops runs its section.
/// </para>
/// </summary>
public static class ViewDrawProgram
{
    public const int GroupOpaqueStaticHuge = 0;
    public const int GroupOpaqueEntityHuge = 1;
    public const int OpaqueBuckets = 4;
    public const int GroupTranslucentEntity = 8;
    public const int GroupOpaqueBrush = 12;

    public const int StudioRender = 0x00000001;
    public const int StudioTwoPass = 0x00000008;
    public const int StudioGenerateStats = 0x01000000;
    public const int StudioSsaoDepthTexture = 0x08000000;
    public const int StudioShadowDepthTexture = 0x40000000;
    public const int StudioTransparency = unchecked((int)0x80000000);

    public const int DfSsaoDepthPass = 0x100;
    public const int DfShadowDepthMap = 0x100000;

    public const int DepthModeShadow = 1;
    public const int DepthModeSsao = 2;

    // DrawOpaqueRenderables_DrawStaticProps' MAX_STATICS_PER_BATCH.
    public const int MaxStaticsPerBatch = 512;

    /// <summary>One list-dependent draw op of the view, as the plan has it.</summary>
    public readonly record struct Draw(RenderOpKind Kind, int I0, int I1, int ViewId);

    /// <summary>What the view's draws are decided with, besides its scene.</summary>
    public sealed record Context(int DrawFlags, IReadOnlyList<Draw> Draws, bool DrawEntities, bool DrawOpaqueRenderables,
                                 bool DrawTranslucentRenderables, bool DrawTranslucentWorld, bool EntityClips, bool VcollideWireframe,
                                 int TranslucentFlags0, int TranslucentFlags1);

    private static bool IsListDraw(RenderOpKind kind)
        => kind is RenderOpKind.DrawOpaque or RenderOpKind.DrawTranslucent or RenderOpKind.DrawNoZTranslucent
                or RenderOpKind.DrawTranslucentWorldInLeaves;

    /// <summary>
    /// The context of the <see cref="RenderOpKind.ViewScene"/> op at
    /// <paramref name="sceneOp"/>: the enclosing view's draw flags and its
    /// list-dependent draws after the op, each with the view id in force when it
    /// runs (the last SetupCurrentView before it, as g_CurrentViewID is).
    /// Null when the op is not a ViewScene inside a view.
    /// </summary>
    public static Context? ContextOf(ReadOnlySpan<RenderOp> plan, int sceneOp, in RenderInputs input)
    {
        if (sceneOp < 0 || sceneOp >= plan.Length || plan[sceneOp].Kind != (int)RenderOpKind.ViewScene)
            return null;

        int depth = 0, drawFlags = -1;
        for (int i = sceneOp - 1; i >= 0 && drawFlags < 0; i--)
        {
            var kind = (RenderOpKind)plan[i].Kind;
            if (kind == RenderOpKind.WorldViewEnd)
                depth++;
            else if (kind == RenderOpKind.WorldViewBegin)
            {
                if (depth == 0)
                    drawFlags = plan[i].GetI(0);
                else
                    depth--;
            }
        }
        if (drawFlags < 0)
            return null;

        int viewId = RenderConst.ViewNone;
        for (int i = sceneOp - 1; i >= 0; i--)
            if (plan[i].Kind == (int)RenderOpKind.SetupCurrentView)
            {
                viewId = plan[i].GetI(0);
                break;
            }

        var draws = new List<Draw>();
        depth = 0;
        for (int i = sceneOp + 1; i < plan.Length; i++)
        {
            var kind = (RenderOpKind)plan[i].Kind;
            if (kind == RenderOpKind.SetupCurrentView)
                viewId = plan[i].GetI(0);
            else if (kind == RenderOpKind.WorldViewBegin)
                depth++;
            else if (kind == RenderOpKind.WorldViewEnd)
            {
                if (depth == 0)
                    break;
                depth--;
            }
            else if (depth == 0 && kind == RenderOpKind.ViewScene)
                break;
            else if (depth == 0 && IsListDraw(kind))
                draws.Add(new Draw(kind, plan[i].GetI(0), plan[i].GetI(1), viewId));
        }

        return new Context(drawFlags, draws,
                           input.Cvar(RenderCvar.r_drawentities) != 0f,
                           input.Cvar(RenderCvar.r_drawopaquerenderables) != 0f,
                           input.Cvar(RenderCvar.r_drawtranslucentrenderables) != 0f,
                           input.Cvar(RenderCvar.r_drawtranslucentworld) != 0f,
                           input.Cvar(RenderCvar.r_entityclips) != 0f,
                           input.Cvar(RenderCvar.vcollide_wireframe) != 0f,
                           plan[sceneOp].GetI(0), plan[sceneOp].GetI(1));
    }

    /// <summary>The view's program: one section per draw of <paramref name="context"/>.</summary>
    public static void Build(ViewSceneData scene, Context context, RenderPlanBuilder b)
    {
        var program = new Program(scene, context, b);
        foreach (Draw draw in context.Draws)
        {
            switch (draw.Kind)
            {
                case RenderOpKind.DrawOpaque: program.Opaque(draw.I0, draw.ViewId); break;
                case RenderOpKind.DrawTranslucent: program.Translucent(draw.I0 != 0, draw.I1 != 0, draw.ViewId); break;
                case RenderOpKind.DrawNoZTranslucent: program.NoZTranslucent(); break;
                case RenderOpKind.DrawTranslucentWorldInLeaves: program.TranslucentWorldInLeaves(draw.I0 != 0); break;
            }
            b.Op(RenderOpKind.SectionEnd);
        }
    }

    private sealed class Program(ViewSceneData scene, Context c, RenderPlanBuilder b)
    {
        private readonly List<int> _detailLeaves = [];
        private readonly List<int> _statics = [];

        private int Start(int group) => scene.Header.GroupStart(group);

        private int Count(int group) => scene.Header.GroupCountOf(group);

        private ref readonly ViewSceneEntry Entry(int e) => ref scene.Entries[e];

        private void Color(in ViewSceneEntry entry)
            => b.Add(RenderOpKind.SetColorModulation, default, [entry.ColorOf(0), entry.ColorOf(1), entry.ColorOf(2)]);

        private void DrawEntity(int e, int flags) => b.Add(RenderOpKind.DrawEntity, [e, flags, c.EntityClips ? 1 : 0]);

        private static int DepthFlags(int depthMode)
            => depthMode == DepthModeShadow ? StudioShadowDepthTexture : depthMode == DepthModeSsao ? StudioSsaoDepthTexture : 0;

        // DrawOpaqueRenderable: the colour, then the draw.
        private void Opaque(int e, bool twoPass, int depthMode, int defaultFlags = 0)
        {
            Color(Entry(e));
            int flags = defaultFlags | StudioRender | DepthFlags(depthMode);
            if (twoPass)
                flags |= StudioTwoPass;
            DrawEntity(e, flags);
        }

        // ---------------------------------------------------------------------
        // CRendering3dView::DrawOpaqueRenderables
        // ---------------------------------------------------------------------
        public void Opaque(int depthMode, int viewId)
        {
            if (!c.DrawOpaqueRenderables || !c.DrawEntities)
                return;

            b.Add(RenderOpKind.SetBlend, default, [1f]);
            b.Op(RenderOpKind.RopeResetCache);
            b.Op(RenderOpKind.ParticleSystemResetCache);

            // The brush models (DrawOpaqueRenderables_DrawBrushModels).
            for (int e = Start(GroupOpaqueBrush), end = e + Count(GroupOpaqueBrush); e < end; e++)
                if (Entry(e).Has(ViewSceneEntryFlags.Renderable))
                    Opaque(e, false, depthMode);

            // Entities then static props, the biggest bucket first.
            for (int bucket = 0; bucket < OpaqueBuckets; bucket++)
            {
                int ents = GroupOpaqueEntityHuge + 2 * bucket;
                for (int e = Start(ents), end = e + Count(ents); e < end; e++)
                    if (Entry(e).Has(ViewSceneEntryFlags.Renderable))
                        Opaque(e, Entry(e).Has(ViewSceneEntryFlags.TwoPass), depthMode);

                StaticProps(GroupOpaqueStaticHuge + 2 * bucket, depthMode, viewId);
            }

            b.Add(RenderOpKind.RopeDrawCache, [depthMode]);
            b.Add(RenderOpKind.ParticleSystemDrawCache, [depthMode]);
        }

        // DrawOpaqueRenderables_DrawStaticProps
        private void StaticProps(int group, int depthMode, int viewId)
        {
            int count = Count(group);
            if (count == 0)
                return;

            b.Add(RenderOpKind.SetColorModulation, default, [1f, 1f, 1f]);
            b.Add(RenderOpKind.SetBlend, default, [1f]);

            _statics.Clear();
            for (int e = Start(group), end = e + count; e < end; e++)
            {
                if (!Entry(e).Has(ViewSceneEntryFlags.Renderable))
                    continue;
                if (viewId == RenderConst.ViewMain && Entry(e).Has(ViewSceneEntryFlags.StudioStats))
                {
                    Opaque(e, false, depthMode, StudioGenerateStats);
                    continue;
                }
                b.Add(RenderOpKind.StaticProp, [e]);
                _statics.Add(e);
                if (_statics.Count == MaxStaticsPerBatch)
                {
                    b.Add(RenderOpKind.DrawStaticPropsBatch, [depthMode, c.VcollideWireframe ? 1 : 0]);
                    _statics.Clear();
                }
            }
            if (_statics.Count > 0)
                b.Add(RenderOpKind.DrawStaticPropsBatch, [depthMode, c.VcollideWireframe ? 1 : 0]);
        }

        // DrawTranslucentRenderable
        private void TranslucentEntity(int e, bool shadowDepth, bool ignoreDepth)
        {
            ref readonly ViewSceneEntry entry = ref Entry(e);
            float blend = entry.FxBlend / 255.0f;
            if (blend <= 0.0f)
                return;
            if (entry.Has(ViewSceneEntryFlags.IgnoresZ) != ignoreDepth)
                return;

            b.Add(RenderOpKind.SetBlend, default, [blend]);
            Color(entry);
            int flags = StudioRender | StudioTransparency;
            if (entry.Has(ViewSceneEntryFlags.TwoPass))
                flags |= StudioTwoPass;
            if (shadowDepth)
                flags |= StudioShadowDepthTexture;
            DrawEntity(e, flags);
        }

        private int TranslucentIndex(int engineFlags)
            => engineFlags == c.TranslucentFlags0 ? 0 : engineFlags == c.TranslucentFlags1 ? 1 : -1;

        private bool LeafTranslucent(int leaf, int engineFlags)
        {
            int k = TranslucentIndex(engineFlags);
            if (k < 0)
                throw new InvalidOperationException($"the scene was not asked about engine flags 0x{engineFlags:x}");
            return scene.Leaves[leaf].TranslucentOf(k);
        }

        private void RenderDetail()
        {
            foreach (int leaf in _detailLeaves)
                b.Add(RenderOpKind.DetailLeaf, [leaf]);
            b.Op(RenderOpKind.DetailRenderTranslucent);
            _detailLeaves.Clear();
        }

        // ---------------------------------------------------------------------
        // CRendering3dView::DrawTranslucentWorldAndDetailPropsInLeaves
        // ---------------------------------------------------------------------
        private void WorldAndDetailInLeaves(int cur, int final, int engineFlags, bool shadowDepth)
        {
            for (; cur >= final; cur--)
            {
                ViewSceneLeaf leaf = scene.Leaves[cur];
                if (LeafTranslucent(cur, engineFlags))
                {
                    RenderDetail();
                    b.Add(RenderOpKind.TranslucentSurfaces, [leaf.ActualLeaf, engineFlags, shadowDepth ? 1 : 0]);
                }
                if (leaf.Detail != 0)
                    _detailLeaves.Add(leaf.Leaf);
            }
        }

        // ---------------------------------------------------------------------
        // CRendering3dView::DrawTranslucentRenderables
        // ---------------------------------------------------------------------
        public void Translucent(bool inSkybox, bool shadowDepth, int viewId)
        {
            if (viewId == RenderConst.ViewMain)
                b.Op(RenderOpKind.UpdateFullScreenDepthTexture);
            else
                b.Add(RenderOpKind.FullScreenDepthTextureValidity, [0]);

            if (!c.DrawTranslucentWorld)
            {
                NoWorld(inSkybox);
                return;
            }

            int prevLeaf = scene.Header.LeafCount - 1;
            _detailLeaves.Clear();
            int engineFlags = (int)RenderPlanText.EngineDrawWorldListFlags(c.DrawFlags & ~RenderConst.DfDrawSkybox);

            b.Op(RenderOpKind.DetailBeginTranslucent);

            if (c.DrawEntities && c.DrawTranslucentRenderables)
            {
                b.Add(RenderOpKind.ParticleSingletons, [inSkybox ? 1 : 0]);

                int start = Start(GroupTranslucentEntity);
                int cur = Count(GroupTranslucentEntity) - 1;
                bool waterTargets = (c.DrawFlags & (RenderConst.DfRenderRefraction | RenderConst.DfRenderReflection)) != 0;
                while (cur >= 0)
                {
                    int thisLeaf = Entry(start + cur).Leaf;
                    WorldAndDetailInLeaves(prevLeaf, thisLeaf, engineFlags, shadowDepth);
                    prevLeaf = thisLeaf - 1;

                    int leafIndex = scene.Leaves[thisLeaf].Leaf;
                    if (scene.Leaves[thisLeaf].Detail != 0)
                    {
                        // Stock's leaf was the last one listed: it draws with
                        // its entities, so the list draws without it.
                        if (_detailLeaves.Count > 0)
                            _detailLeaves.RemoveAt(_detailLeaves.Count - 1);
                        RenderDetail();
                        for (; cur >= 0 && Entry(start + cur).Leaf == thisLeaf; --cur)
                        {
                            int e = start + cur;
                            b.Add(RenderOpKind.DetailRenderInLeaf, [leafIndex, e]);
                            if (FrameBuffer(e, shadowDepth, waterTargets, 0))
                                continue;
                            TranslucentEntity(e, shadowDepth, false);
                        }
                        b.Add(RenderOpKind.DetailRenderInLeaf, [leafIndex, -1]);
                    }
                    else
                    {
                        RenderDetail();
                        for (; cur >= 0 && Entry(start + cur).Leaf == thisLeaf; --cur)
                        {
                            int e = start + cur;
                            if (FrameBuffer(e, shadowDepth, waterTargets, 1))
                                continue;
                            TranslucentEntity(e, shadowDepth, false);
                        }
                    }
                    _detailLeaves.Clear();
                }
            }

            WorldAndDetailInLeaves(prevLeaf, 0, engineFlags, shadowDepth);
            RenderDetail();
            b.Add(RenderOpKind.SetBlend, default, [1f]);
        }

        // The frame-buffer refresh before a translucent entity that samples it;
        // true when the entity is skipped (a water target's view draws none).
        private bool FrameBuffer(int e, bool shadowDepth, bool waterTargets, int rule)
        {
            ref readonly ViewSceneEntry entry = ref Entry(e);
            bool pow2 = entry.Has(ViewSceneEntryFlags.PowerOfTwoFrameBuffer);
            bool full = entry.Has(ViewSceneEntryFlags.FullFrameBuffer);
            if (!(pow2 || full) || shadowDepth)
                return false;
            if (waterTargets)
                return true;
            b.Add(RenderOpKind.FrameBufferForEntity, [pow2 ? 1 : 0, full ? 1 : 0, rule]);
            return false;
        }

        // ---------------------------------------------------------------------
        // CRendering3dView::DrawTranslucentRenderablesNoWorld
        // ---------------------------------------------------------------------
        private void NoWorld(bool inSkybox)
        {
            if (!c.DrawEntities || !c.DrawTranslucentRenderables)
                return;
            b.Add(RenderOpKind.ParticleSingletons, [inSkybox ? 1 : 0]);
            EachTranslucent(ignoreDepth: false);
        }

        // ---------------------------------------------------------------------
        // CRendering3dView::DrawNoZBufferTranslucentRenderables
        // ---------------------------------------------------------------------
        public void NoZTranslucent()
        {
            if (!c.DrawEntities || !c.DrawTranslucentRenderables)
                return;
            EachTranslucent(ignoreDepth: true);
        }

        private void EachTranslucent(bool ignoreDepth)
        {
            bool shadowDepth = (c.DrawFlags & (DfShadowDepthMap | DfSsaoDepthPass)) != 0;
            int start = Start(GroupTranslucentEntity);
            for (int cur = Count(GroupTranslucentEntity) - 1; cur >= 0; --cur)
            {
                int e = start + cur;
                if (Entry(e).Has(ViewSceneEntryFlags.PowerOfTwoFrameBuffer))
                    b.Op(RenderOpKind.UpdateRefractTexture);
                if (Entry(e).Has(ViewSceneEntryFlags.FullFrameBuffer))
                    b.Op(RenderOpKind.UpdateScreenEffectTexture);
                TranslucentEntity(e, shadowDepth, ignoreDepth);
            }
            b.Add(RenderOpKind.SetBlend, default, [1f]);
        }

        // ---------------------------------------------------------------------
        // CRendering3dView::DrawTranslucentWorldInLeaves (stock asks with the
        // view's own draw flags, not the engine's: carried as stock has it)
        // ---------------------------------------------------------------------
        public void TranslucentWorldInLeaves(bool shadowDepth)
        {
            for (int cur = scene.Header.LeafCount - 1; cur >= 0; cur--)
                if (LeafTranslucent(cur, c.DrawFlags))
                    b.Add(RenderOpKind.TranslucentSurfaces, [scene.Leaves[cur].ActualLeaf, c.DrawFlags, shadowDepth ? 1 : 0]);
        }
    }
}
