//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// func_breakable_surf: a pane of glass that shatters a piece at a time.
///
/// <para>
/// THE WHOLE ENTITY IS HERE NOW. It used to be the one class left on a
/// CBaseEntity shell, and the argument for leaving it there was that its pane
/// grid is 256 networked bools and its cracks are drawn by a renderer. Both have
/// answers: a class on the hierarchy can declare a table of its own, and the
/// renderer moved to the client shell. What is left in C++ is the mesh building,
/// which was never a decision.
/// </para>
///
/// <para>
/// What lives here is every rule: how big the panes are, which one a shot broke,
/// what holds the rest up, and what falls when it stops holding. See
/// <see cref="PaneGrid"/>, which has all of it and 30 tests.
/// </para>
///
/// <para>
/// It is a CBreakable, as stock's CBreakableSurface is: health, its inputs and
/// OnBreak and OnHealthChanged are <see cref="BreakableBrush"/>'s, and glass with
/// no health spawns at one and takes no damage. What it changes is how a hit
/// lands. A traced hit (a bullet, a club, a blast) comes to its own
/// TraceAttack, which counts it against its health and shatters panes whatever
/// its take-damage mode says; TakeDamage only breaks it whole, for a crush, a
/// blast or a slash; and being killed does nothing.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncBreakableSurf, ScriptedEntityKind.Brush)]
public sealed class FuncBreakableSurf : BreakableBrush, ITraceAttacked
{
    /// <summary>Stock's Spawn sets MOVETYPE_PUSH.</summary>
    public override bool MovesAsPusher => true;

    public FuncBreakableSurf(IServerPropData propData, IEngineSound sound, IEnginePrecache precache, ITempEnts temps,
                             IEngineHost host, IEngineDamage damage, IEngineWorldQuery world, IEngineEffects effects,
                             IBlastSweep blast, IServerDecals decals, IEngineEntityPhysics physics,
                             IEntityRoster<ITraceAttacked> traceAttacked, PhysicsDamageQueue impacts,
                             ConVarRegistry? convars = null)
        : base(propData, sound, precache, temps, host, damage, world, effects, blast, decals, physics, convars)
    {
        World = world;
        Effects = effects;
        TraceAttacked = traceAttacked;
        Impacts = impacts;
    }

    /// <summary>The step's impact damage, delivered after it (PhysCallbackDamage).</summary>
    private readonly PhysicsDamageQueue Impacts;

    /// <summary>SF_BREAKABLESURF_CRACK_DECALS: a light knock leaves a mark.</summary>
    private const int CrackDecals = 1;

    /// <summary>SF_BREAKABLESURF_DAMAGE_FROM_HELD_OBJECTS: what a player carries can break it.</summary>
    private const int DamageFromHeldObjects = 2;

    /// <summary>The shots' roster, which a broken pane is on: a bullet passing through it still shatters panes.</summary>
    private readonly IEntityRoster<ITraceAttacked> TraceAttacked;

    /// <summary>asking the world what is there.</summary>
    private readonly IEngineWorldQuery World;

    /// <summary>The mark a shot leaves, which is the one thing here a player hears.</summary>
    private readonly IEngineEffects Effects;
    /// <summary>What a map may set on this entity. See <see cref="FuncBreakableSurfVars"/>.</summary>
    [ScriptVars] public FuncBreakableSurfVars SurfaceVars = new();

    /// <summary>What the client is told. Bound to this entity by the registry.</summary>
    [NetState] public BreakableSurfaceNetState State = new();

    /// <summary>The grid, which does not exist until the sheet goes.</summary>
    private PaneGrid? _grid;

    /// <summary>m_bIsBroken: the sheet has gone, and its panes are what is left.</summary>
    private bool _isBroken;

    /// <summary>Tile does not fall apart on its own; glass does.</summary>
    private bool IsTile => SurfaceVars.SurfaceType == 1;

    /// <summary>
    /// WHAT THIS PANE IS MADE OF, as a surfaceprop index. -1 until spawn.
    ///
    /// <para>
    /// Not the surfaceprop of the face a shot struck, which is what a trace
    /// reports and is wrong here five times out of six: vbsp will not compile a
    /// breakable surface unless exactly ONE of its faces is drawn, so the other
    /// five carry nodraw's — 0, the default, which is concrete. Measured on
    /// ss_sandbox, the same pane reads GLASS/GLASSWINDOWBREAK070A from the front
    /// and TOOLS/TOOLSNODRAW from four units behind it.
    /// </para>
    ///
    /// <para>
    /// So the entity says what it is made of and the struck face is not asked.
    /// Stock arrives at the same place from the other end: CBreakableSurface's
    /// TraceAttack dispatches its glass effect from the ENTITY, on any face,
    /// without consulting the trace at all.
    /// </para>
    ///
    /// <para>
    /// Carried across a hot reload because it is resolved ONCE, by the
    /// <see cref="ResolveSurfaceProp"/> the spawn runs, which a reload does not
    /// repeat. Uncarried, the rebuilt instance reverted to <c>-1</c> and every
    /// shard broken after a <c>cs_sv_reload</c> flew with no glass effect
    /// (OnSpawnFieldReloadTests).
    /// </para>
    /// </summary>
    [SaveField] public int SurfaceProp { get; private set; } = -1;

    /// <summary>
    /// The same answer as a letter — 'Y' for glass — which is what a reader and
    /// an assertion can both check. A surfaceprop index is opaque and depends on
    /// what the game happened to load.
    /// </summary>
    public SurfaceMaterial GameMaterial => World.SurfacePropGameMaterial(SurfaceProp);

    /// <summary>
    /// Works out <see cref="SurfaceProp"/>: the model first, the map key second.
    ///
    /// <para>
    /// THE MODEL IS THE EXACT ANSWER. A breakable surface draws with exactly one
    /// material by construction, and that material's $surfaceprop is what the
    /// mapper textured the pane with — the same material stock's Spawn reads
    /// $crackmaterial off.
    /// </para>
    ///
    /// <para>
    /// THE MAP KEY IS THE ONE THAT ALWAYS ANSWERS. A side with no materials
    /// loaded cannot be asked about a model, and surfacetype is stock's own
    /// glass-or-tile switch — the same key that picks the shard models, the
    /// crack sprites and whether the sheet settles at all. Coarser than the
    /// material, and never worse than concrete.
    /// </para>
    /// </summary>
    private void ResolveSurfaceProp()
    {
        SurfaceProp = World.EntityModelSurfaceProp(Handle);

        if (SurfaceProp < 0)
            SurfaceProp = World.SurfacePropIndex(IsTile ? "tile" : "glass");
    }

    public override void OnSpawn()
    {
        // CBreakable::Spawn first: its health, solidity and static object.
        base.OnSpawn();

        if (SurfaceVars.QuadError != 0)
        {
            // VBSP could not divide the face, so there is no grid to be had and
            // there never will be. Stock removes the entity; so does this, and
            // says which of its two complaints it was.
            Log.Warning($"[SourceSharp] func_breakable_surf: vbsp reported error {SurfaceVars.QuadError} " +
                           $"(1 = several drawn faces, 2 = the drawn face is not a quad); removed\n");
            Remove();
            return;
        }

        // CBreakableSurface::Spawn: glass's own group.
        SetCollisionGroup(CollisionGroup.BreakableGlass);

        State.SurfaceType = (byte)SurfaceVars.SurfaceType;

        ResolveSurfaceProp();
        TraceAttacked.Add(this);

        Log.Msg($"[SourceSharp/C#] func_breakable_surf is made of surfaceprop {SurfaceProp} " +
                $"(game material '{(char)GameMaterial}')\n");
    }

    public override void OnRemoved()
    {
        TraceAttacked.Remove(this);
        base.OnRemoved();
    }

    /// <summary>
    /// CBreakableSurface::OnTakeDamage: the blow itself takes nothing (the
    /// traced hit already counted). A crush breaks the sheet, and glass breaks
    /// to a blast or a slash, each from where it came.
    /// </summary>
    public override int OnDamagedAt(EntityHandle attacker, float damage, DamageType damageType,
                                    Vector position, Vector force, HitGroup hitGroup = HitGroup.Generic)
    {
        if (!_isBroken && damageType == DamageType.Crush)
        {
            Die(attacker, force);
            return 0;
        }

        if (!IsTile && (damageType & (DamageType.Blast | DamageType.Slash)) != 0)
            Die(attacker, force);

        return 0;
    }

    /// <summary>CBreakableSurface::Event_Killed: nothing; a sheet is never removed for its health.</summary>
    public override void OnKilled(EntityHandle attacker) { }

    /// <summary>
    /// CBreakableSurface::TraceAttack. Every traced hit counts against its
    /// health and says so, and breaks the sheet if it has not gone. A bullet
    /// or a club takes out the pane it struck and what that disturbs. A blast
    /// blows out a patch of tile, or the whole of a glass window.
    /// </summary>
    public void TraceAttack(EntityHandle attacker, float damage, DamageType damageType, Vector start, Vector end)
    {
        int health = (int)(Health - damage);
        Health = health;
        OnHealthChanged.Fire((float)health);

        bool shot = (damageType & (DamageType.Bullet | DamageType.Club)) != 0;
        Vector along = end - start;

        // BEFORE the sheet goes, because breaking it makes it a trigger and the
        // client re-traces this ray to place its decal -- through a pane that is
        // no longer there, onto the wall behind it.
        if (shot && !_isBroken)
            Impact(end, along, damageType);

        if (!_isBroken)
            Die(attacker, along);

        if (_grid is null)
            return;

        if (shot)
        {
            var (w, h) = _grid.PanePos(end);
            foreach (var (pw, ph) in _grid.PanesHitAt(w, h, spreads: !IsTile, coin: () => _random.Next(0, 2)))
                BreakPane(pw, ph);
        }
        else if ((damageType & (DamageType.Sonic | DamageType.Blast)) != 0)
        {
            if (IsTile)
            {
                // A roughly circular patch of tile, with some randomness, about
                // the attacker (or where the blast met the sheet).
                var (fw, fh) = _grid.PanePos(attacker != EntityHandle.None ? Entities.GetOrigin(attacker) : end);
                int cw = (int)fw, ch = (int)fh;
                for (int pw = cw - 4; pw < cw + 4; pw++)
                {
                    for (int ph = ch - 4; ph < ch + 4; ph++)
                    {
                        if (Math.Abs(cw - pw) + Math.Abs(ch - ph) < _random.Next(2, 6) && pw >= 0 && ph >= 0
                            && pw < _grid.Wide && ph < _grid.High)
                            BreakPane(pw, ph);
                    }
                }
            }
            else
            {
                // Glass: the whole window goes.
                foreach (var (pw, ph) in _grid.BreakAll())
                    State.SetPaneSolid(pw, ph, false);
            }
        }

        // What is left has to settle: taking a pane out is what makes its
        // neighbours unstable, and nothing else asks the question.
        if (!IsTile)
            SetNextThink(0.1f);
    }

    /// <summary>
    /// TraceAttackToTriggers' half: once broken the sheet is a trigger, and a
    /// shot through the plane of its panes, within them, crosses it there.
    /// </summary>
    public bool ShotThrough(Vector start, Vector end, out Vector hit)
    {
        hit = default;
        if (!_isBroken || _grid is null)
            return false;

        Vector normal = _grid.Normal;
        Vector along = end - start;
        float across = along.Dot(normal);
        if (MathF.Abs(across) < 1e-6f)
            return false;

        float t = (_grid.Corner - start).Dot(normal) / across;
        if (t < 0f || t > 1f)
            return false;

        hit = start + along * t;
        var (w, h) = _grid.PanePos(hit);
        return w >= 0f && h >= 0f && w < _grid.Wide && h < _grid.High;
    }

    /// <summary>
    /// InputShatter: "x y radius". The sheet breaks if it has not, and every
    /// pane whose centre is within the radius (in units) of the point x across
    /// and y up it (fractions of the sheet) is taken out. Stock measures a
    /// pane's height in panel widths, and so does this.
    /// </summary>
    [ScriptedEvent("Shatter")]
    private void OnShatterInput(InputData input)
    {
        ScriptedVarParse.Vector(input.Value, out Vector shatter);

        if (!_isBroken)
            Die(EntityHandle.None, Vector.Zero);

        if (_grid is null)
            return;

        float centerX = shatter.X * _grid.Wide;
        float centerY = shatter.Y * _grid.High;

        int minX = Math.Max(0, (int)(centerX - shatter.Z / _grid.PanelWidth));
        int maxX = Math.Min(_grid.Wide, (int)(centerX + shatter.Z / _grid.PanelWidth) + 1);
        int minY = Math.Max(0, (int)(centerY - shatter.Z / _grid.PanelHeight));
        int maxY = Math.Min(_grid.High, (int)(centerY + shatter.Z / _grid.PanelHeight) + 1);

        float actualX = centerX * _grid.PanelWidth, actualY = centerY * _grid.PanelHeight;
        for (int w = minX; w < maxX; w++)
        {
            for (int h = minY; h < maxY; h++)
            {
                float dx = (w + 0.5f) * _grid.PanelWidth - actualX;
                float dy = (h + 0.5f) * _grid.PanelWidth - actualY;
                if (dx * dx + dy * dy <= shatter.Z * shatter.Z)
                    BreakPane(w, h);
            }
        }

        if (!IsTile)
            SetNextThink(0.1f);
    }

    /// <summary>
    /// CBreakableSurface::VPhysicsCollision: while whole, an impact is damage
    /// by the glass table (the default for tile). Over 10 the sheet's own mass
    /// is set to 2, "to get correct collision response for the object
    /// breaking this", and the damage is dealt after the step: a crush, which
    /// breaks it. A lighter knock leaves a mark where the map asks for one.
    /// </summary>
    public override void OnVPhysicsCollision(in PhysicsEvent ev, int index)
    {
        if (!_isBroken)
        {
            uint otherRaw = index == 0 ? ev.Entity1 : ev.Entity0;
            var other = otherRaw == PhysicsEvent.NoEntity ? EntityHandle.None : new EntityHandle(otherRaw);
            var parties = new ImpactParties(SelfIsPlayer: false,
                                            OtherIsPlayer: other != EntityHandle.None && Entities.IsPlayer(other),
                                            OtherIsWorld: other == EntityHandle.None, SelfIsFloating: false);

            float damage = PhysicsImpactDamage.Calculate(in ev, index, IsTile ? ImpactDamageTables.Npc : ImpactDamageTables.Glass,
                                                         1f, allowStaticDamage: false,
                                                         damageFromHeldObjects: (SpawnFlags & DamageFromHeldObjects) != 0,
                                                         parties, out DamageType damageType);

            if (damage > 10f)
            {
                Physics.SetMass(Handle, 2f);
                Impacts.Add(Handle, other, damage, damageType, in ev, index);
            }
            else if (damage > 0f && (SpawnFlags & CrackDecals) != 0)
            {
                Vector normal = index == 0 ? ev.Normal * -1f : ev.Normal;
                var mark = World.TraceLineSurface(ev.Point - normal, ev.Point + normal, TraceMask.Brushes, EntityHandle.None);
                if (mark.Hit && mark.Entity == Handle)
                    Effects.ImpactEffect(ev.Point - normal, mark.End, mark.SurfaceProps, DamageType.Club, Handle);
            }
        }

        base.OnVPhysicsCollision(in ev, index);
    }

    /// <summary>Once broken the sheet is a trigger, and whatever passes through it is touching it every frame.</summary>
    public override bool TouchEveryTick => _isBroken || base.TouchEveryTick;

    /// <summary>
    /// CBreakableSurface::SurfaceTouch, once the sheet has broken (CBreakable's
    /// touch before). Whatever passes through takes out the panes its box
    /// covers on the sheet, and now and then the one either side of each row.
    /// Tile gives only to something moving at 500 or more.
    /// </summary>
    public override void OnTouch(EntityHandle toucher)
    {
        if (!_isBroken || _grid is null)
        {
            base.OnTouch(toucher);
            return;
        }

        if (IsTile && Entities.GetVelocity(toucher).Length < 500f)
            return;

        // The toucher's box, projected on to the sheet as stock does it.
        Vector origin = Entities.GetOrigin(toucher);
        var (mins, maxs) = Entities.GetBounds(toucher);
        var (maxsW, maxsH) = _grid.PanePos(OnPlane(origin + maxs));
        var (minsW, minsH) = _grid.PanePos(OnPlane(origin + mins));

        int minWidth = (int)MathF.Floor(MathF.Max(0f, MathF.Min(maxsW, minsW)));
        int maxWidth = (int)MathF.Ceiling(MathF.Min(_grid.Wide, MathF.Max(maxsW, minsW)));
        int minHeight = (int)MathF.Floor(MathF.Max(0f, MathF.Min(maxsH, minsH)));
        int maxHeight = (int)MathF.Ceiling(MathF.Min(_grid.High, MathF.Max(maxsH, minsH)));

        bool broke = false;
        for (int h = minHeight; h < maxHeight; h++)
        {
            // Randomly break the one before and after so it does not look square.
            if (_random.Next(0, 2) == 1)
                broke |= BreakPane(minWidth - 1, h);

            for (int w = minWidth; w < maxWidth; w++)
                broke |= BreakPane(w, h);

            if (_random.Next(0, 2) == 1)
                broke |= BreakPane(maxWidth + 1, h);
        }

        if (broke && !IsTile)
            SetNextThink(0.1f);
    }

    /// <summary>SurfaceTouch's projection: the point moved along the sheet's normal by its distance from the corner's plane.</summary>
    private Vector OnPlane(Vector point) => point + _grid!.Normal * _grid.Normal.Dot(point - _grid.Corner);

    private bool BreakPane(int w, int h)
    {
        if (!_grid!.Break(w, h))
            return false;

        State.SetPaneSolid(w, h, false);
        return true;
    }

    /// <summary>
    /// Takes out the pane that was hit, and whatever else that shot disturbs.
    ///
    /// A shot with no position cannot pick a pane — nothing on this path
    /// produces one, but a future damage source might, and picking the corner
    /// pane would be worse than picking none.
    /// </summary>
    /// <summary>Which way the sheet faces, from the four corners vbsp wrote.</summary>
    private Vector PaneNormal
        => (SurfaceVars.LowerLeft - SurfaceVars.LowerRight).Cross(SurfaceVars.LowerLeft - SurfaceVars.UpperLeft).Normalized;

    /// <summary>How far back along the shot the impact's start point is put.</summary>
    private const float ImpactBackoff = 32f;

    /// <summary>
    /// The mark a shot leaves: the engine's own impact, carrying THIS ENTITY's
    /// surfaceprop instead of the struck face's.
    ///
    /// <para>
    /// That substitution is the whole of it. The client picks the impact sound
    /// from the surfaceprop the server sent — PlayImpactSound reads that before
    /// anything it traced for itself — so a pane that says "glass" sounds like
    /// glass from either side, where the nodraw back of the brush says concrete.
    /// </para>
    ///
    /// <para>
    /// The start point is stepped back along the damage force, which points the
    /// way the shot was going, so it lands on the side the shot came from — and
    /// which side matters here, the two faces of this brush being different
    /// materials. With no force to step along (nothing on this path produces
    /// that, but a future damage source might) the pane's own normal is used,
    /// which picks a side rather than collapsing the ray to a point.
    /// </para>
    /// </summary>
    private void Impact(Vector position, Vector force, DamageType damageType)
    {
        Vector along = force.Length > 0f ? force.Normalized : PaneNormal * -1f;

        Effects.ImpactEffect(position - along * ImpactBackoff, position, SurfaceProp,
                             damageType, Handle);
    }

    private readonly Random _random = new();

    /// <summary>
    /// CBreakableSurface::Die. The sheet goes: no health left, OnBreak, the
    /// grid is worked out, the client is told, and the surface stops being
    /// something to walk into (its physics object goes with its solidity).
    ///
    /// The geometry happens HERE and not at spawn, which is stock's choice and a
    /// good one — a window nobody ever breaks pays for none of it.
    /// </summary>
    private void Die(EntityHandle attacker, Vector attackDir)
    {
        if (_isBroken)
            return;

        _isBroken = true;
        Health = 0;
        OnBreak.Fire(attacker != EntityHandle.None ? attacker : Handle);

        var quad = new PaneGrid.Quad(SurfaceVars.LowerLeft, SurfaceVars.LowerRight, SurfaceVars.UpperLeft, SurfaceVars.UpperRight);

        _grid = PaneGrid.Derive(quad, PaneNormal, attackDir, Angles.RightUp);

        // Told in one go, because the client cannot read half a grid: the pane
        // bits are indexed by a fixed stride, but the dimensions decide how many
        // of them mean anything.
        State.Wide        = (byte)_grid.Wide;
        State.High        = (byte)_grid.High;
        State.PanelWidth  = _grid.PanelWidth;
        State.PanelHeight = _grid.PanelHeight;
        State.Normal      = _grid.Normal;
        State.Corner      = _grid.Corner;
        State.FillPanes();
        State.Broken      = true;

        // Walked through rather than into, from now on. A shattered window is a
        // hole with edges, and stock does the same.
        Entities.DestroyPhysics(Handle);
        SetSolidity(BrushSolidity.Trigger);

        Log.Msg($"[SourceSharp/C#] func_breakable_surf broke into {_grid.Wide}x{_grid.High} " +
                   $"panes of {_grid.PanelWidth:F1}x{_grid.PanelHeight:F1}\n");

        // Glass settles; tile just sits there with holes in it.
        if (!IsTile)
            SetNextThink(0.1f);
    }

    /// <summary>
    /// One settle: whatever can no longer hold itself up lets go, and the
    /// entity keeps thinking while anything is still moving.
    /// </summary>
    public override void OnThink()
    {
        if (_grid is null)
            return;

        var falling = _grid.Settle(SurfaceVars.Fragility);

        foreach (var (w, h) in falling)
        {
            if (_grid.Break(w, h))
                State.SetPaneSolid(w, h, false);
        }

        // Only while something happened. A window that has finished coming apart
        // must stop thinking, or every broken pane in the map costs a tick
        // forever.
        if (falling.Count > 0)
            SetNextThink(0.1f);
    }
}

/// <summary>
/// The map keys <see cref="FuncBreakableSurf"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncBreakableSurfVars
{
    //-------------------------------------------------------------------------
    // What the map wrote.
    //
    // The four corners are VBSP's, not a mapper's: it works out the drawn face
    // and writes them, and writes "error" when the brush is not a single quad it
    // can divide. A surface that ignored that would network pane dimensions
    // nothing computed.
    //-------------------------------------------------------------------------
    public Vector LowerLeft { get; set; }

    public Vector LowerRight { get; set; }

    public Vector UpperLeft { get; set; }

    public Vector UpperRight { get; set; }

    /// <summary>VBSP's verdict. Non-zero means it could not divide the face.</summary>
    [VarName("error")]
    public int QuadError { get; set; }

    /// <summary>How readily the rest lets go once pieces start falling. 100 is stock glass.</summary>
    public int Fragility { get; set; } = 100;

    /// <summary>0 glass, 1 tile. It decides the materials and whether panes fall at all.</summary>
    public int SurfaceType { get; set; }
}
