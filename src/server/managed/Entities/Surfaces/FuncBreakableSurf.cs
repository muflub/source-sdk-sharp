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
/// </summary>
[ScriptedEntity(EntityNames.FuncBreakableSurf, ScriptedEntityKind.Brush)]
public sealed class FuncBreakableSurf : ScriptedEntity
{
    public FuncBreakableSurf(IEngineWorldQuery world, IEngineEffects effects)
    {
        World = world;
        Effects = effects;
    }

    /// <summary>asking the world what is there.</summary>
    private readonly IEngineWorldQuery World;

    /// <summary>The mark a shot leaves, which is the one thing here a player hears.</summary>
    private readonly IEngineEffects Effects;
    /// <summary>What a map may set on this entity. See <see cref="FuncBreakableSurfVars"/>.</summary>
    [ScriptVars] public FuncBreakableSurfVars Vars = new();

    public readonly Output OnBreak = new("OnBreak");

    /// <summary>
    /// Named without the "On" because the class also OVERRIDES OnDamaged, and a
    /// field cannot share a name with a method. Of the two it is the field that
    /// can be renamed without changing what a map writes.
    /// </summary>
    public readonly Output Damaged = new("OnDamaged");

    /// <summary>What the client is told. Bound to this entity by the registry.</summary>
    [NetState] public BreakableSurfaceNetState State = new();

    /// <summary>The grid, which does not exist until the sheet goes.</summary>
    private PaneGrid? _grid;

    private bool _broken;

    /// <summary>Tile does not fall apart on its own; glass does.</summary>
    private bool IsTile => Vars.SurfaceType == 1;

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
    public SurfaceMaterial Material => World.SurfacePropGameMaterial(SurfaceProp);

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
        if (Vars.QuadError != 0)
        {
            // VBSP could not divide the face, so there is no grid to be had and
            // there never will be. Stock removes the entity; so does this, and
            // says which of its two complaints it was.
            Log.Warning($"[SourceSharp] func_breakable_surf: vbsp reported error {Vars.QuadError} " +
                           $"(1 = several drawn faces, 2 = the drawn face is not a quad); removed\n");
            Remove();
            return;
        }

        if (Vars.Health > 0)
            Health = Vars.Health;

        State.SurfaceType = (byte)Vars.SurfaceType;

        ResolveSurfaceProp();

        Log.Msg($"[SourceSharp/C#] func_breakable_surf is made of surfaceprop {SurfaceProp} " +
                $"(game material '{(char)Material}')\n");
    }

    /// <summary>
    /// How much of a hit counts against the surface as a whole, and which pane
    /// it took out on the way.
    ///
    /// <para>
    /// Returning zero is not "ignore this": it is what a surface with no health
    /// key does, and it leaves the stock behaviour in place — the pane that was
    /// struck shatters and the sheet stays up. A blast, a crush or a slash takes
    /// the whole thing down whatever the health says, which is stock's rule and
    /// the only way a bullet-proof sheet can ever be cleared.
    /// </para>
    /// </summary>
    public override int OnDamagedAt(EntityHandle attacker, float damage, DamageType damageType,
                                    Vector position, Vector force)
    {
        if (_broken)
            return 0;

        // Whole-sheet damage. Stock's list, unchanged: these are the ways glass
        // goes all at once rather than a piece at a time.
        const DamageType WholeSheet = DamageType.Blast | DamageType.Crush | DamageType.Slash;

        if ((damageType & WholeSheet) != 0)
        {
            Break(attacker, force);
            return 0;
        }

        // A shot. Stock does this in TraceAttack, which the hierarchy has no
        // equivalent of — the position on the damage is what replaces it.
        //
        // THE FIRST SHOT BREAKS THE SHEET. Not visibly: breaking is what works
        // the grid out, and until there is a grid there is no pane to take out.
        // Stock does the same, in the same order, for the same reason.
        if ((damageType & (DamageType.Bullet | DamageType.Club)) != 0)
        {
            ShatterAt(attacker, position, force, damageType);
            return DamageTaken(damage);
        }

        return DamageTaken(damage);
    }

    /// <summary>
    /// How much a hit counts against the surface as a whole, which is nothing
    /// unless the map asked for a health model.
    /// </summary>
    private int DamageTaken(float damage)
    {
        int taken = (int)damage;

        if (Vars.Health <= 0 || taken < Vars.MinimumDamage)
            return 0;

        Damaged.Fire();
        return taken;
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
        => (Vars.LowerLeft - Vars.LowerRight).Cross(Vars.LowerLeft - Vars.UpperLeft).Normalized;

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

    private void ShatterAt(EntityHandle attacker, Vector position, Vector force,
                           DamageType damageType)
    {
        if (position.X == 0f && position.Y == 0f && position.Z == 0f)
            return;

        // BEFORE the sheet goes, because breaking it makes it a trigger and the
        // client re-traces this ray to place its decal — through a pane that is
        // no longer there, onto the wall behind it.
        Impact(position, force, damageType);

        Break(attacker, force);

        if (_grid is null)
            return;

        var (w, h) = _grid.PanePos(position);

        foreach (var (pw, ph) in _grid.PanesHitAt(w, h, spreads: !IsTile, coin: () => _random.Next(0, 2)))
        {
            if (_grid.Break(pw, ph))
                State.SetPaneSolid(pw, ph, false);
        }

        // What is left has to settle: taking a pane out is what makes its
        // neighbours unstable, and nothing else asks the question.
        if (!IsTile)
            SetNextThink(0.1f);
    }

    private readonly Random _random = new();

    public override void OnKilled(EntityHandle attacker) => Break(attacker, Vector.Zero);

    /// <summary>
    /// The sheet goes: the grid is worked out, the client is told, and the
    /// surface stops being something to walk into.
    ///
    /// The geometry happens HERE and not at spawn, which is stock's choice and a
    /// good one — a window nobody ever breaks pays for none of it.
    /// </summary>
    private void Break(EntityHandle attacker, Vector attackDir)
    {
        if (_broken)
            return;

        _broken = true;

        var quad = new PaneGrid.Quad(Vars.LowerLeft, Vars.LowerRight, Vars.UpperLeft, Vars.UpperRight);

        _grid = PaneGrid.Derive(quad, PaneNormal, attackDir, World.AngleVectorsFromForward);

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
        SetSolidity(BrushSolidity.Trigger);

        OnBreak.Fire();

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

        var falling = _grid.Settle(Vars.Fragility);

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

    /// <summary>
    /// Shots the whole surface takes before it goes, over and above the panes
    /// shattering individually.
    ///
    /// Zero — the default, and what every stock map writes — means the surface
    /// has no health at all and behaves exactly as it does in stock. That
    /// fallthrough is the point: a map that never heard of this gets stock.
    /// </summary>
    public int Health { get; set; }

    /// <summary>Damage below this is ignored entirely, not reduced.</summary>
    [VarName("minhealthdmg")]
    public int MinimumDamage { get; set; }
}
