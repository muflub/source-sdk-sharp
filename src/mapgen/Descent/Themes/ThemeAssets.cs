namespace SourceSharp.MapGen.Descent.Themes;

/// <summary>One asset a theme names: a brush material or a model, the role it plays, and the model's data when it is one.</summary>
/// <param name="Kind"><c>material</c> for a brush face, <c>static</c> for a <c>prop_static</c>, <c>dynamic</c> for a <c>prop_dynamic</c>.</param>
public sealed record ThemeAsset(string Asset, string Kind, string Role, PropModel? Model = null);

/// <summary>
/// What a theme names (plan E7, task B2), listed so its depth's palette file
/// and the asset database can be held to it: every surface, the door frame
/// and sill, every lamp fixture and every prop, and the same of its way-in
/// theme under an <c>arrival-</c> role. The theme's own plug material and
/// the tool trigger are not assets of the database.
/// </summary>
public static class ThemeAssets
{
    /// <summary>The palette file a depth's theme is recorded in, under <c>src/mapgen/Descent/Themes/palettes</c>.</summary>
    public static string PaletteFile(int depth) => $"d{depth:00}.tsv";

    /// <summary>Every asset <paramref name="theme"/> names, each once per role.</summary>
    public static IEnumerable<ThemeAsset> Of(RoomTheme theme, string prefix = "")
    {
        yield return new(theme.AssetOf(theme.Floor.Material), "material", prefix + "floor");
        yield return new(theme.Wall.Material, "material", prefix + "wall");
        yield return new(theme.Ceiling.Material, "material", prefix + "ceiling");
        yield return new(theme.AssetOf(theme.Block.Material), "material", prefix + "block");
        if (theme.Trim is { } trim)
            yield return new(trim.Material, "material", prefix + "trim");
        if (theme.WallLow is { } low)
            yield return new(low.Material, "material", prefix + "wall-low");
        if (theme.LargeWall is { } large)
            yield return new(large.Material, "material", prefix + "large-wall");
        if (theme.Bands?.Surface is { } band)
            yield return new(band.Material, "material", prefix + "band");
        if (theme.WallSeam is { } seam)
            yield return new(seam.Surface.Material, "material", prefix + "seam");
        if (theme.Hazard is { } hazard)
            yield return new(hazard.Material, "material", prefix + "hazard");
        FrameModel frame = theme.DoorFrame;
        if (theme.FrameBrushes is { } brushes)
        {
            yield return new(brushes.Post.Material, "material", prefix + "door-frame");
            if (brushes.Head.Material != brushes.Post.Material)
                yield return new(brushes.Head.Material, "material", prefix + "door-frame-head");
            if (brushes.HeadFace is { } stripe)
                yield return new(stripe.Material, "material", prefix + "door-frame-stripe");
            if (brushes.Crossing is { } planks)
                yield return new(planks.Material, "material", prefix + "crossing");
        }
        else
            yield return new(Strip(frame.Model), "static", prefix + "door-frame");
        if (frame.Sill is { } sill)
            yield return new(Strip(sill.Model), "static", prefix + "door-sill");
        if (theme.Lamps is { } lamps)
            yield return Model(lamps.Fixture, prefix + "lamp");
        if (theme.TallLamps is { } tall)
            yield return Model(tall.Fixture, prefix + "tall-lamp");
        if (theme.CoverLamps is { } cover)
            yield return Model(cover.Fixture, prefix + "cover-lamp");
        if (theme.WorkLamps is { } work)
            yield return Model(work.Kind.Fixture, prefix + "work-lamp");
        if (theme.RoutePools is { } pools)
            yield return Model(pools.Fixture, prefix + "route-lamp");
        if (theme.Pendants is { } pendants)
        {
            yield return Model(pendants.Fixture, prefix + "pendant");
            foreach (PropModel cord in pendants.Cords)
                yield return Model(cord, prefix + "pendant-cord");
        }

        foreach (LampKind? k in (LampKind?[])[theme.Lamps, theme.TallLamps, theme.CoverLamps, theme.WorkLamps?.Kind])
            if (k?.Stand is { } stand)
                yield return Model(stand, prefix + "lamp-stand");
        PropSet p = theme.Props;
        foreach (PropModel m in p.Wall)
            yield return Model(m, prefix + "wall-prop");
        foreach (PropModel m in p.Corner)
            yield return Model(m, prefix + "corner-pile");
        foreach (PropModel m in p.Floor)
            yield return Model(m, prefix + "floor-prop");
        if (p.CeilingRun is { } run)
            yield return Model(run, prefix + "ceiling-run");
        if (p.Rail is { } rail)
            yield return Model(rail, prefix + "rail");
        foreach (PropModel m in p.RailEnds)
            yield return Model(m, prefix + "rail");
        if (p.RailBend is { } bend)
            yield return Model(bend, prefix + "rail");
        if (p.RailStop is { } stop)
            yield return Model(stop, prefix + "rail");
        foreach (HungModel h in p.Hung.DistinctBy(h => h.Model.Model))
            yield return Model(h.Model, prefix + "hung");
        if (p.Siding is { } siding)
            yield return Model(siding, prefix + "siding");
        foreach (PropModel m in p.Companions)
            yield return Model(m, prefix + "companion");
        foreach (PropModel m in p.Centrepiece)
            yield return Model(m, prefix + "centrepiece");
        foreach (PropModel m in p.Hanging)
            yield return Model(m, prefix + "hanging");
        if (theme.Water is { } water)
            yield return new(theme.AssetOf(water), "water", prefix + "water");
        if (theme.HazardFloor is { } floorHazard)
            yield return new(theme.AssetOf(floorHazard.Surface), floorHazard.Liquid ? "water" : "material", prefix + "hazard-" + floorHazard.Kind);
        if (theme.HazardFloor?.Lips is { } lips)
            yield return new(lips.Material, "material", prefix + "hazard-lips");
        if (theme.Gantry?.Hook is { } hook)
            yield return Model(hook, prefix + "gantry-hook");
        if (theme.Gantry?.Cable is { } cable)
            yield return Model(cable, prefix + "gantry-cable");
        foreach (Surface s in theme.ArenaSurfaces)
            yield return new(theme.AssetOf(s.Material), "material", prefix + "arena-surface");
        foreach (LampKind k in theme.ArenaLamps)
            yield return Model(k.Fixture, prefix + "arena-lamp");
        foreach (PropModel m in theme.SetPieces)
            yield return Model(m, prefix + "set-piece");
        if (theme.Arrival is { } arrival && prefix.Length == 0)
            foreach (ThemeAsset a in Of(arrival, "arrival-"))
                yield return a;
        // The cave families' theme is the depth's with other walls and floor: only those differ.
        if (theme.Caves is { } caves && prefix.Length == 0)
        {
            yield return new(caves.AssetOf(caves.Floor.Material), "material", "caves-floor");
            yield return new(caves.Wall.Material, "material", "caves-wall");
            yield return new(caves.Ceiling.Material, "material", "caves-ceiling");
            if (caves.LargeWall is { } caveLarge)
                yield return new(caveLarge.Material, "material", "caves-large-wall");
            if (caves.WallLow is { } caveLow)
                yield return new(caveLow.Material, "material", "caves-wall-low");
        }
    }

    private static ThemeAsset Model(PropModel m, string role) => new(Strip(m.Model), m.Static ? "static" : "dynamic", role, m);

    /// <summary>A model path as a palette writes it: without <c>models/</c> and <c>.mdl</c>.</summary>
    public static string Strip(string model) =>
        model.StartsWith("models/", StringComparison.Ordinal) && model.EndsWith(".mdl", StringComparison.Ordinal) ? model[7..^4] : model;
}
