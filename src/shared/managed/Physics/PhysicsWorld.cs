//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// The world's physics objects, stock's <c>PhysCreateWorld_Shared</c> and
/// <c>PhysCreateVirtualTerrain</c> (game/shared/physics_shared.cpp), and the
/// server's <c>PhysCreateWorld</c> before it (static props first).
/// </summary>
public static class PhysicsWorldBuilder
{
    /// <summary>bspfile.h's <c>MAX_MAP_DISPINFO</c> (the PC arm).</summary>
    public const int MaxMapDispInfo = 2048;

    /// <summary>The world model's index: <c>modelinfo-&gt;GetVCollide( 1 )</c>.</summary>
    public const int WorldModelIndex = 1;

    /// <summary>The materialtable block's size: stock's <c>int surfaceTable[128]</c>.</summary>
    public const int MaterialTableSize = 128;

    /// <summary>
    /// The world's static props (when <paramref name="staticProps"/>: the server's
    /// <c>PhysCreateWorld</c>), then <c>PhysCreateWorld_Shared( pWorld,
    /// modelinfo-&gt;GetVCollide( 1 ), defaults )</c>. The world's game data is
    /// <paramref name="gameData"/>, 0 on both sides (the world is not an entity
    /// the game data names). Answers the world's object, or 0.
    /// </summary>
    public static nint Create(IPhysicsWorldVPhysics vp, nint gameData, in ObjectParams defaults, bool staticProps)
    {
        ArgumentNullException.ThrowIfNull(vp);

        nint environment = vp.Environment();

        // Stock asks staticpropmgr before PhysCreateWorld_Shared's own physenv
        // test; with no environment the engine would be handed NULL, so nothing
        // is asked at all.
        if (environment == 0)
            return 0;

        if (staticProps)
            vp.CreateStaticPropPhysics(environment);

        VCollideView collide = vp.Collide(WorldModelIndex);
        if (collide.Pointer == 0 || collide.SolidCount < 1)
            return 0;

        int surfaceData = vp.SurfaceIndex("default");

        ObjectParams worldParams = defaults;
        worldParams.GameData = gameData;
        worldParams.Name = vp.NativeText("world");

        nint world = vp.CreatePolyObjectStatic(vp.CollideSolid(collide, 0), surfaceData, worldParams);

        // hint - saves vphysics some work
        if (world != 0)
            vp.MarkNeverDeleted(world);

        // walk the world keys in case there are some fluid volumes to create
        nint parser = vp.KeyParserCreate(collide.KeyValues);
        bool createVirtualTerrain = false;
        Span<int> surfaceTable = stackalloc int[MaterialTableSize];

        while (!vp.Finished(parser))
        {
            string block = vp.BlockName(parser);

            if (Is(block, "solid") || Is(block, "staticsolid"))
            {
                ParsedSolid solid = vp.ParseSolid(parser);
                ObjectParams solidParams = solid.Params;
                solidParams.EnableCollisions = 1;
                solidParams.GameData = gameData;
                solidParams.Name = vp.NativeText("world");
                surfaceData = vp.SurfaceIndex("default");

                // already created world above
                if (solid.Index == 0)
                    continue;

                // Stock reads solids[ index ] before it tests the index against
                // the count; the range test comes first here, with the same
                // answer for every index the read was defined for.
                nint solidCollide = vp.CollideSolid(collide, solid.Index);
                if (solid.Index >= collide.SolidCount || solidCollide == 0)
                {
                    // this implies that the collision model is a mopp and the physics DLL doesn't support that.
                    createVirtualTerrain = true;
                    continue;
                }

                // create this as part of the world
                nint obj = vp.CreatePolyObjectStatic(solidCollide, surfaceData, solidParams);

                // invalid collision model or can't create, ignore
                if (obj == 0)
                    continue;

                vp.MarkNeverDeleted(obj);
                vp.SetContents(obj, solid.ContentsMask);

                if (world == 0)
                    world = obj;
            }
            else if (Is(block, "fluid"))
            {
                ParsedFluid fluid = vp.ParseFluid(parser);

                // create a fluid for floating
                if (fluid.Index > 0 && fluid.Index < collide.SolidCount)
                {
                    ObjectParams fluidParams = defaults;   // copy world's params
                    fluidParams.EnableCollisions = 1;
                    fluidParams.Name = vp.NativeText("fluid");
                    fluidParams.GameData = gameData;
                    surfaceData = vp.SurfaceIndex(fluid.SurfaceProp);

                    // create this as part of the world
                    nint water = vp.CreatePolyObjectStatic(vp.CollideSolid(collide, fluid.Index), surfaceData, fluidParams);

                    if (water != 0)
                    {
                        vp.MarkNeverDeleted(water);
                        vp.CreateFluidController(water, gameData);
                    }
                }
            }
            else if (Is(block, "materialtable"))
            {
                surfaceTable.Clear();

                vp.ParseSurfaceTable(parser, surfaceTable);
                vp.SetWorldMaterialIndexTable(surfaceTable);
            }
            else if (Is(block, "virtualterrain"))
            {
                createVirtualTerrain = true;
                vp.SkipBlock(parser);
            }
            else
            {
                // unknown chunk???
                vp.SkipBlock(parser);
            }
        }

        vp.KeyParserDestroy(parser);

        if (createVirtualTerrain && vp.SupportsVirtualMesh())
            CreateVirtualTerrain(vp, gameData, defaults);

        return world;
    }

    /// <summary>
    /// Stock's <c>PhysCreateVirtualTerrain</c>: one static object per
    /// displacement the engine has a virtual-terrain collide for, named
    /// <c>vdisp_%04d</c>, on the "default" surface.
    /// </summary>
    public static int CreateVirtualTerrain(IPhysicsWorldVPhysics vp, nint gameData, in ObjectParams defaults)
    {
        ArgumentNullException.ThrowIfNull(vp);

        if (vp.Environment() == 0)
            return 0;

        int created = 0;
        for (int i = 0; i < MaxMapDispInfo; i++)
        {
            nint collide = vp.VirtualTerrainCollide(i);
            if (collide == 0)
                continue;

            ObjectParams terrainParams = defaults;
            terrainParams.EnableCollisions = 1;
            terrainParams.GameData = gameData;
            terrainParams.Name = vp.NativeText(DispName(i));
            int surfaceData = vp.SurfaceIndex("default");

            // create this as part of the world
            nint obj = vp.CreatePolyObjectStatic(collide, surfaceData, terrainParams);
            if (obj != 0)
            {
                vp.MarkNeverDeleted(obj);
                created++;
            }
        }

        return created;
    }

    /// <summary><c>Q_snprintf( nameBuf, sizeof( nameBuf ), "vdisp_%04d", i )</c>.</summary>
    public static string DispName(int index) => "vdisp_" + index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);

    // strcmpi: an ASCII case-insensitive compare.
    private static bool Is(string block, string name) => string.Equals(block, name, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Stock's <c>PhysModelParseSolidByIndex( solid, pEntity, modelIndex, solidIndex )</c>
/// (physics_shared.cpp): the model's solid block with the given index (any,
/// when it is negative), parsed with the solid defaults, into the caller's
/// <c>solid_t</c>. The caller names no entity: the game data and debug name are
/// the caller's to fill, as stock's arm with a NULL entity left them.
/// </summary>
public static class ModelSolidParse
{
    public static bool Parse(IPhysicsWorldVPhysics vp, int modelIndex, int solidIndex, nint solid)
    {
        ArgumentNullException.ThrowIfNull(vp);

        VCollideView collide = vp.Collide(modelIndex);
        if (collide.Pointer == 0 || solid == 0)
            return false;

        bool parsed = false;
        vp.ResetSolid(solid);

        nint parser = vp.KeyParserCreate(collide.KeyValues);
        while (!vp.Finished(parser))
        {
            if (string.Equals(vp.BlockName(parser), "solid", StringComparison.OrdinalIgnoreCase))
            {
                ParsedSolid candidate = vp.ParseSolid(parser);

                if (solidIndex < 0 || candidate.Index == solidIndex)
                {
                    parsed = true;
                    vp.CopyParsedSolid(solid);
                    break;
                }
            }
            else
            {
                vp.SkipBlock(parser);
            }
        }

        vp.KeyParserDestroy(parser);

        // collisions are off by default
        vp.SetSolidEnableCollisions(solid, true);
        return parsed;
    }
}

/// <summary>
/// Stock's <c>PrecachePhysicsSounds</c> (physics_shared.cpp): every surface's
/// ten sound names precached as soundscripts, and the handle each answered
/// written back into the surface, where the impact, scrape and step sounds
/// read it.
/// </summary>
public static class SurfaceSoundPrecache
{
    /// <summary>The count of <c>surfacesoundnames_t</c> / <c>surfacesoundhandles_t</c> fields.</summary>
    public const int SoundsPerSurface = 10;

    /// <summary>soundemittersystembase's <c>SOUNDEMITTER_INVALID_HANDLE</c>.</summary>
    public const short InvalidHandle = -1;

    /// <summary>Precaches every set name through <paramref name="precacheScript"/>; answers the count of names precached.</summary>
    public static int Run(ISurfaceSoundTable table, Func<string, short> precacheScript)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(precacheScript);

        Span<ushort> names = stackalloc ushort[SoundsPerSurface];
        Span<short> handles = stackalloc short[SoundsPerSurface];
        int precached = 0;

        // precache the surface prop sounds
        int count = table.Count();
        for (int i = 0; i < count; i++)
        {
            if (!table.SoundNames(i, names))
                continue;

            for (int k = 0; k < SoundsPerSurface; k++)
            {
                // Only precache if a value was set in the script file...
                handles[k] = InvalidHandle;
                if (names[k] != 0)
                {
                    handles[k] = precacheScript(table.Text(names[k]) ?? string.Empty);
                    precached++;
                }
            }

            table.WriteHandles(i, handles);
        }

        return precached;
    }
}

/// <summary>One friction contact as <c>PhysComputeSlideDirection</c> reads it.</summary>
public readonly record struct SlideContact(Vector Normal, bool OtherMoveable, float OtherMass);

/// <summary>
/// Stock's <c>PhysComputeSlideDirection</c> (physics_shared.cpp): the velocity
/// clipped against each contact with something immovable or heavier than
/// <c>minMass</c>, and (when asked for) the angular velocity reduced to its
/// part along that contact's normal.
/// </summary>
public static class PhysicsSlideDirection
{
    public static (Vector Velocity, Vector AngularVelocity) Compute(Vector inputVelocity, Vector inputAngularVelocity,
                                                                    ReadOnlySpan<SlideContact> contacts, float minMass,
                                                                    bool clipAngular)
    {
        Vector velocity = inputVelocity;
        Vector angVel = inputAngularVelocity;

        foreach (SlideContact contact in contacts)
        {
            if (contact.OtherMoveable && !(contact.OtherMass > minMass))
                continue;

            Vector normal = contact.Normal;

            // BUGBUG: Figure out the correct rotation clipping equation
            if (clipAngular)
                angVel = normal * angVel.Dot(normal);

            // Determine how far along plane to slide based on incoming direction.
            // NOTE: Normal points away from this object
            float proj = velocity.Dot(normal);
            if (proj > 0.0f)
                velocity -= normal * proj;
        }

        return (velocity, angVel);
    }
}
