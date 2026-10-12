//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The grid of panes a <c>func_breakable_surf</c> shatters one piece at a time.
///
/// <para>
/// This is the arithmetic the surface was left out of the parallel hierarchy
/// for. It is not "how glass is drawn" — that is the renderer, and it stays
/// native — it is WHICH pieces fall and when, which is a rule, and rules belong
/// here.
/// </para>
///
/// <para>
/// Deliberately free of the engine: it takes the four corner vertices the map
/// wrote and a basis function, and answers with indices and support values.
/// Everything about it can be checked without a game running, which matters
/// because the alternative way to find out whether the support propagation is
/// right is to shoot a window and watch.
/// </para>
///
/// <para>
/// The numbers are stock's, to the digit. A window that shatters differently
/// from the one every Half-Life 2 map was built around is a divergence nobody
/// asked for.
/// </para>
/// </summary>
public sealed class PaneGrid
{
    /// <summary>The grid is at most this on a side, and stock says so.</summary>
    public const int MaxPanels = 16;

    /// <summary>How many units of surface one pane covers, before rounding.</summary>
    public const float PanelSize = 12f;

    /// <summary>The most support a pane can have from its eight neighbours.</summary>
    public const float MaxSupport = 6.75f;

    /// <summary>Normalised support below which a pane falls.</summary>
    public const float BreakSupport = 0.20f;

    /// <summary>Support of a pane that has gone. Distinct from zero on purpose.</summary>
    public const float PaneBroken = -1f;

    /// <summary>Support of an untouched pane.</summary>
    public const float PaneHealthy = 1f;

    /// <summary>
    /// Which way is right and which is up, for a surface facing a direction.
    ///
    /// Supplied rather than computed here, and that is not squeamishness about
    /// trigonometry: the CLIENT works the same two vectors out from the same
    /// normal, in native code, to place the panes it draws. Two implementations
    /// of VectorAngles and AngleVectors agreeing to the last bit is not
    /// something to assume, and the failure would be panes drawn a fraction of a
    /// pane away from the ones that broke.
    /// </summary>
    public delegate (Vector Right, Vector Up) Basis(Vector forward);

    public int Wide { get; private set; }
    public int High { get; private set; }
    public float PanelWidth { get; private set; }
    public float PanelHeight { get; private set; }

    /// <summary>Which way the surface faces, after the side facing the attacker was chosen.</summary>
    public Vector Normal { get; private set; }

    /// <summary>The corner panes are counted from.</summary>
    public Vector Corner { get; private set; }

    /// <summary>How many panes have gone. Stock counts these and so does this.</summary>
    public int BrokenCount { get; private set; }

    private readonly float[,] _support = new float[MaxPanels, MaxPanels];

    private Vector _widthDir;
    private Vector _heightDir;

    /// <summary>The four corners of the drawn face, as the map's keyvalues give them.</summary>
    public readonly record struct Quad(Vector LowerLeft, Vector LowerRight,
                                       Vector UpperLeft, Vector UpperRight);

    /// <summary>
    /// Works out the grid from the face and the direction the killing blow came
    /// from.
    ///
    /// <para>
    /// A surface has two sides and only one of them is shattered: the side the
    /// damage came from. When the blow arrives from behind, the whole quad is
    /// pushed one unit along the normal and the normal is flipped, which is what
    /// puts the broken face where the shooter is standing.
    /// </para>
    ///
    /// <para>
    /// This is stock's <c>CBreakableSurface::Die</c>, which does the geometry at
    /// the moment of breaking rather than at spawn — so a surface that is never
    /// broken never pays for any of it.
    /// </para>
    /// </summary>
    public static PaneGrid Derive(Quad quad, Vector normal, Vector attackDir, Basis basis)
    {
        var grid = new PaneGrid();

        Vector ll = quad.LowerLeft, lr = quad.LowerRight;
        Vector ul = quad.UpperLeft, ur = quad.UpperRight;

        // Which side gets shattered.
        //
        // Note what this does NOT do, because it is surprising and stock does
        // the same: flipping the normal here has no lasting effect, since the
        // normal is recomputed from the face's own edges a few lines down. What
        // survives the branch is the quad moving one unit towards the attacker —
        // which is what puts the broken face on the side it was shot from — and
        // `dir`, which flips the handedness of the width/height swap below.
        float dir = -1f;

        if (attackDir.LengthSquared > 0.001f && normal.Dot(attackDir) < 0f)
        {
            ll += normal; lr += normal; ul += normal; ur += normal;
            normal *= -1f;
            dir = 1f;
        }

        Vector width  = ll - lr;
        Vector height = ll - ul;

        // The normal is the cross of the two edges, not the one that arrived.
        // The one that arrived came from a trace and points at the shooter.
        grid.Normal = width.Cross(height).Normalized;

        var (widthDir, heightDir) = basis(grid.Normal * -1f);
        grid._widthDir  = widthDir;
        grid._heightDir = heightDir;

        // A face whose "width" edge runs up and down. Swapping is what makes a
        // window lying on its side count panes the way it is drawn.
        if (MathF.Abs(widthDir.Dot(width)) < 0.5f)
        {
            Vector saved = height;
            height = width * dir;
            width  = saved * dir;
        }

        bool left  = widthDir.Dot(width)   < 0f;
        bool lower = heightDir.Dot(height) < 0f;

        grid.Corner = left ? (lower ? ll : ul)
                           : (lower ? lr : ur);

        float faceWidth  = width.Length;
        float faceHeight = height.Length;

        // Integer division, then the pane size is whatever divides the face
        // evenly — so a window is never left with a sliver of a pane at the end.
        grid.Wide = Math.Clamp((int)(faceWidth  / PanelSize), 1, MaxPanels);
        grid.High = Math.Clamp((int)(faceHeight / PanelSize), 1, MaxPanels);

        grid.PanelWidth  = faceWidth  / grid.Wide;
        grid.PanelHeight = faceHeight / grid.High;

        // Every pane, not just the ones in range: the whole array is reset so a
        // surface broken twice cannot inherit support from the first grid.
        for (int w = 0; w < MaxPanels; w++)
            for (int h = 0; h < MaxPanels; h++)
                grid._support[w, h] = PaneHealthy;

        return grid;
    }

    /// <summary>
    /// True for a pane that has gone, and for one that was never there.
    ///
    /// Out of range counts as broken deliberately: it is what makes the support
    /// arithmetic below treat the world past the edge of the window as nothing
    /// to lean on, without a bounds check at each of its eight neighbours.
    /// </summary>
    public bool IsBroken(int w, int h)
    {
        if (w < 0 || w >= Wide)  return true;
        if (h < 0 || h >= High)  return true;

        return _support[w, h] == PaneBroken;
    }

    /// <summary>Support, floored at zero — a broken pane holds nothing up.</summary>
    public float Support(int w, int h) => MathF.Max(0f, _support[w, h]);

    public void SetSupport(int w, int h, float support) => _support[w, h] = support;

    /// <summary>
    /// What holds one pane up, from its eight neighbours and the frame.
    ///
    /// <para>
    /// The weights are stock's and they are not symmetrical, which is the whole
    /// character of the effect: the pane below counts for 1.25 and the two above
    /// for 0.25, so a window falls DOWNWARDS as it comes apart instead of
    /// eroding evenly. The frame counts as a full neighbour on every side but
    /// the top corners.
    /// </para>
    ///
    /// <para>
    /// The comment stock leaves here is worth keeping: it assumes the window is
    /// upright. One lying flat still works, it just simulates less convincingly.
    /// </para>
    /// </summary>
    public float RecalcSupport(int w, int h)
    {
        // Never zero. Zero is what a broken pane reports, and a healthy pane
        // with no neighbours left must still be distinguishable from one.
        float support = 0.01f;

        support += (h == High - 1) ? 1.0f  : Support(w, h + 1);
        support += (h == 0)        ? 1.25f : 1.25f * Support(w, h - 1);
        support += (w == 0)        ? 1.0f  : Support(w - 1, h);
        support += (w == Wide - 1) ? 1.0f  : Support(w + 1, h);

        support += (h == 0 || w == 0)        ? 1.0f : Support(w - 1, h - 1);
        support += (h == 0 || w == Wide - 1) ? 1.0f : Support(w + 1, h - 1);

        support += (h == High - 1 || w == Wide - 1) ? 0.25f : 0.25f * Support(w + 1, h + 1);
        support += (h == High - 1 || w == 0)        ? 0.25f : 0.25f * Support(w - 1, h + 1);

        return support;
    }

    /// <summary>
    /// Marks a pane gone. Returns false if it had already gone or never existed,
    /// so a caller can tell whether anything actually happened.
    ///
    /// <para>
    /// IDEMPOTENT, and stock is not: <c>BreakPane</c> increments its broken
    /// count every time it is called, including on a pane that had already gone.
    /// Nothing stock does with that count notices, but "how many panes are left"
    /// is the obvious thing to ask it, and a count that can exceed the grid
    /// answers wrongly. Breaking twice is a thing that happens — a shot and the
    /// instability sweep can reach the same pane in one tick.
    /// </para>
    /// </summary>
    public bool Break(int w, int h)
    {
        // One check covers both cases: out of range reads as already broken.
        if (IsBroken(w, h))
            return false;

        BrokenCount++;
        SetSupport(w, h, PaneBroken);
        return true;
    }

    /// <summary>Takes the whole sheet down, and answers which panes went.</summary>
    public List<(int W, int H)> BreakAll()
    {
        var gone = new List<(int, int)>();

        for (int w = 0; w < Wide; w++)
            for (int h = 0; h < High; h++)
                if (Break(w, h))
                    gone.Add((w, h));

        return gone;
    }

    /// <summary>
    /// Recomputes every pane's support and returns the ones that can no longer
    /// hold themselves up.
    ///
    /// <para>
    /// Two passes, and it has to be two: support is read from neighbours, so
    /// writing it back as it is computed would let a pane earlier in the sweep
    /// influence a later one within the same settle. Stock does the same and for
    /// the same reason.
    /// </para>
    ///
    /// <para>
    /// Fragility scales the threshold: 100 is stock glass, lower holds together
    /// longer. The caller decides what happens to a pane that falls — stock
    /// drops a physical shard half the time and simply shatters it the rest.
    /// </para>
    /// </summary>
    public List<(int W, int H)> Settle(int fragility)
    {
        var falling = new List<(int, int)>();
        var recalculated = new float[MaxPanels, MaxPanels];

        for (int w = 0; w < Wide; w++)
            for (int h = 0; h < High; h++)
                if (!IsBroken(w, h))
                    recalculated[w, h] = RecalcSupport(w, h);

        float threshold = BreakSupport * (fragility / 100f);

        for (int w = 0; w < Wide; w++)
        {
            for (int h = 0; h < High; h++)
            {
                if (IsBroken(w, h))
                    continue;

                SetSupport(w, h, recalculated[w, h] / MaxSupport);

                if (_support[w, h] < threshold)
                    falling.Add((w, h));
            }
        }

        return falling;
    }

    /// <summary>
    /// Which pane a point on the surface belongs to, as fractional pane counts
    /// from the corner. Out-of-range answers are the caller's to reject — a shot
    /// that misses the glass still has a position.
    /// </summary>
    public (float W, float H) PanePos(Vector world)
    {
        Vector fromCorner = world - Corner;

        return (_widthDir.Dot(fromCorner)  / PanelWidth,
                _heightDir.Dot(fromCorner) / PanelHeight);
    }

    /// <summary>
    /// Which panes a shot at this point takes out.
    ///
    /// <para>
    /// Not just the one it hit, and the extras are what make a bullet hole in
    /// glass look like a bullet hole rather than a missing square. A shot landing
    /// near the EDGE of a pane takes the neighbour it was nearly in as well, and
    /// a coin flip takes the pane above, and if that came up, the one above that
    /// — so damage runs upward in a ragged line instead of leaving a tidy hole.
    /// </para>
    ///
    /// <para>
    /// Stock's rule, thresholds included: 0.8 and 0.2 of a pane's width. Only
    /// glass spreads; tile loses exactly the piece that was hit.
    /// </para>
    ///
    /// <para>
    /// The coin flips are a delegate so this can be tested. Two calls, in this
    /// order, each 0 or 1 — the same sequence stock asks random for.
    /// </para>
    /// </summary>
    public List<(int W, int H)> PanesHitAt(float fractionalW, float fractionalH,
                                           bool spreads, Func<int> coin)
    {
        var hit = new List<(int, int)>();

        int w = (int)fractionalW;
        int h = (int)fractionalH;

        void Take(int tw, int th)
        {
            if (!IsBroken(tw, th) && !hit.Contains((tw, th)))
                hit.Add((tw, th));
        }

        Take(w, h);

        if (!spreads)
            return hit;

        // How far into its own pane the shot landed. Near a side, the pane on
        // that side goes too.
        float acrossW = fractionalW - w;
        float acrossH = fractionalH - h;

        if (acrossW > 0.8f && w != Wide - 1) Take(w + 1, h);
        else if (acrossW < 0.2f && w != 0)   Take(w - 1, h);

        if (acrossH > 0.8f && h != High - 1) Take(w, h + 1);
        else if (acrossH < 0.2f && h != 0)   Take(w, h - 1);

        // Upward, sometimes. Glass falls down, so damage climbs.
        if (coin() == 0)
        {
            Take(w, h + 1);

            if (coin() == 0)
                Take(w, h + 2);
        }

        return hit;
    }

    /// <summary>Where a pane's own corner is in the world, for spawning debris.</summary>
    public Vector PaneOrigin(int w, int h)
        => Corner + _widthDir * (w * PanelWidth) + _heightDir * (h * PanelHeight);
}
