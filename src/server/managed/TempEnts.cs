//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// <see cref="ITempEnts"/> over the wire: packs a blob from a declaration and
/// asks native to play it.
///
/// <para>
/// IT LIVES ON THE SERVER, and SideAuditTests is what decided that rather than
/// taste. <c>TempEntPlayback</c> is a server-only ABI slot — the client half of
/// this design PLAYS what arrives instead of sending it — and shared code that
/// leans on a slot only one side fills is a call that silently does nothing on
/// the other. The INTERFACE stays in the shared assembly, because both sides
/// answer "make sparks happen"; only this implementation of it is the wire.
/// </para>
///
/// <para>
/// It holds a <see cref="TempEntRegistry"/> and nothing else. There is no
/// per-effect code here and there is not meant to be: a method looks its class
/// up by name, writes its fields by name, and sends. The knowledge of what a
/// spark IS lives in <c>TeSparks</c>, which is a declaration, and in the
/// client's play path, which is a different assembly.
/// </para>
///
/// <para>
/// A CLASS NATIVE REFUSED IS A NO-OP THAT SAYS SO ONCE. Warning per spark would
/// fill a console in a firefight; warning never would make a whole effect
/// disappear silently, which is precisely the failure this port is scored
/// against.
/// </para>
/// </summary>
public sealed class TempEnts : ITempEnts
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly TempEntRegistry _registry;
    private readonly IEngineEffects _engine;
    private readonly HashSet<string> _complained = new(StringComparer.Ordinal);

    public TempEnts(ILog log, TempEntRegistry registry, IEngineEffects engine)
    {

        _log = log;
        _registry = registry;
        _engine = engine;
    }


    /// <inheritdoc/>
    public void Sparks(TeFilter filter, float delay, Vector position,
                       int magnitude, int trailLength, Vector? direction)
    {
        // Origin is DT_TEParticleSystem's and the other three are DT_TESparks'
        // own; both are in this class's blob, because the base nests at
        // datatable offset zero and its props read at their own offsets out of
        // the same object. The declaration inherits, so the caller sets them
        // together and never spells a wire name.
        //
        // vec3_origin for a null direction is stock's "no particular direction"
        // rather than a direction of zero length that would have to be
        // special-cased downstream (te_sparks.cpp:84).
        var state = new TeSparks
        {
            Origin      = position,
            Magnitude   = magnitude,
            TrailLength = trailLength,
            Direction   = direction ?? Vector.Zero,
        };

        TempEntType? type = Resolve(state);

        if (type is null)
            return;

        _engine.TempEntPlayback(type.Handle, state.ToBlob(type), filter, delay);
    }

    /// <inheritdoc/>
    public void MetalSparks(TeFilter filter, float delay, Vector position, Vector direction)
    {
        var state = new TeMetalSparks { Position = position, Direction = direction };

        TempEntType? type = Resolve(state);

        if (type is null)
            return;

        _engine.TempEntPlayback(type.Handle, state.ToBlob(type), filter, delay);
    }

    /// <inheritdoc/>
    public void EnergySplash(TeFilter filter, float delay, Vector position,
                             Vector direction, bool explosive)
    {
        // THE DECLARATION FORM. It used to name the class as a string and then
        // write three fields by their stock wire names -- "m_vecPos",
        // "m_vecDir", "m_bExplosive" -- none of which the compiler checked and
        // any one of which could have been left out, which is an effect at the
        // world origin that looks like a wire bug. The declaration says all
        // three at once, by member, and TempEntState.ToBlob warns if it wrote
        // nothing.
        //
        // The bool is still one bit on the wire and one BYTE in the blob, which
        // is what stock's CNetworkVar( bool ) is: the writer puts a value down
        // at the field's DECLARED storage width, and EntityLayout takes that
        // width from the C# bool. Describing it as four bytes would put three
        // bytes of whatever follows on the wire, and this is the last field.
        var state = new TeEnergySplash
        {
            Position  = position,
            Direction = direction,
            Explosive = explosive,
        };

        TempEntType? type = Resolve(state);

        if (type is null)
            return;

        _engine.TempEntPlayback(type.Handle, state.ToBlob(type), filter, delay);
    }


    // ------------------------------------------------------------------
    // The decals (plan Part 6, lane B). Six tables, three different entity
    // index widths, and the inventory's one qangles prop — all of which is
    // said in the DECLARATIONS. Nothing here knows any of it: a method fills
    // in the declaration, which resolves its own class from the [TempEntity]
    // on it, and hands over the blob it lays out.
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    public void BSPDecal(TeFilter filter, float delay, Vector position, int entity, int index)
    {
        var state = new TeBspDecal
        {
            Origin = position,
            Entity = entity,
            Index  = index,
        };

        TempEntType? type = Resolve(state);

        if (type is null)
            return;

        _engine.TempEntPlayback(type.Handle, state.ToBlob(type), filter, delay);
    }

    /// <inheritdoc/>
    public void ProjectDecal(TeFilter filter, float delay, Vector position, QAngle angles,
                             float distance, int index)
    {
        var state = new TeProjectedDecal
        {
            Origin = position,

            // THREE FLOATS EITHER WAY, and the difference is on the wire rather
            // than in the blob: SendPropQAngles anglemods each component into its
            // declared [0,360] on the way out (plan 2B). The declaration says
            // TePropKind.QAngles — the one kind a Vector member cannot infer —
            // and the blob is three floats whichever way it is encoded.
            Rotation = angles.AsVector(),

            Distance = distance,
            Index    = index,
        };

        TempEntType? type = Resolve(state);

        if (type is null)
            return;

        _engine.TempEntPlayback(type.Handle, state.ToBlob(type), filter, delay);
    }

    /// <inheritdoc/>
    public void Decal(TeFilter filter, float delay, Vector position, Vector start,
                      int entity, int hitbox, int index)
    {
        var state = new TeDecal
        {
            Origin = position,
            Start  = start,
            Entity = entity,
            Hitbox = hitbox,
            Index  = index,
        };

        TempEntType? type = Resolve(state);

        if (type is null)
            return;

        _engine.TempEntPlayback(type.Handle, state.ToBlob(type), filter, delay);
    }

    /// <inheritdoc/>
    public void FootprintDecal(TeFilter filter, float delay, Vector origin, Vector right,
                               int entity, int index, byte materialType)
    {
        var state = new TeFootprintDecal
        {
            Origin = origin,

            // te_footprintdecal.cpp:87 normalised this AFTER assigning it and
            // BEFORE Create(), so what went on the wire was always a unit vector.
            // The comment travels with the behaviour: the client builds the print's
            // basis from it and an unnormalised direction stretches the decal.
            Direction = right.Normalized,

            Entity       = entity,
            Index        = index,
            MaterialType = materialType,
        };

        TempEntType? type = Resolve(state);

        if (type is null)
            return;

        _engine.TempEntPlayback(type.Handle, state.ToBlob(type), filter, delay);
    }

    /// <inheritdoc/>
    public void PlayerDecal(TeFilter filter, float delay, Vector position, int player, int entity)
    {
        // BY MEMBER, so the order these three are set in carries no meaning at
        // all — which is what lets the DECLARATION follow stock's TABLE order
        // (origin, entity, player) where the C++ class body has m_nPlayer first.
        // Declaration order is wire order; initialiser order is not.
        var state = new TePlayerDecal
        {
            Origin = position,
            Entity = entity,
            Player = player,
        };

        TempEntType? type = Resolve(state);

        if (type is null)
            return;

        _engine.TempEntPlayback(type.Handle, state.ToBlob(type), filter, delay);
    }

    /// <inheritdoc/>
    public void WorldDecal(TeFilter filter, float delay, Vector position, int index)
    {
        var state = new TeWorldDecal
        {
            Origin = position,
            Index  = index,
        };

        TempEntType? type = Resolve(state);

        if (type is null)
            return;

        _engine.TempEntPlayback(type.Handle, state.ToBlob(type), filter, delay);
    }

    // ------------------------------------------------------------------
    // Fizz (plan Part 6, lane D), and BEHAVIOUR LEDGER ROW S1.
    //
    // The one method on this class whose SIGNATURE is not stock's, and the
    // deviation is the row: ITempEntsSystem::Fizz takes a const CBaseEntity *
    // and TE_Fizz a const IServerEntity *, neither of which survives
    // CBaseEntity becoming an alias for IGameEntity. DT_TEFizz has only ever
    // put an edict INDEX on the wire, so an int is the whole of what is
    // needed and the effect comes back with no interface at all.
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    public void Fizz(TeFilter filter, float delay, int entIndex,
                     int modelIndex, int density, int current)
    {
        var state = new TeFizz
        {
            // The edict index, and nothing about the entity beyond it. See the
            // block comment above and ITempEnts.Fizz.
            Entity     = entIndex,
            ModelIndex = modelIndex,
            Density    = density,

            // Signed on the wire: SendPropInt(SENDINFO(m_nCurrent), 16) passes no
            // flags argument, so a current running the other way along the axis is
            // a negative number rather than a very fast positive one.
            Current = current,
        };

        TempEntType? type = Resolve(state);

        if (type is null)
            return;

        _engine.TempEntPlayback(type.Handle, state.ToBlob(type), filter, delay);
    }

    // Lane F's three behaviour-ledger rows (plan Part 1.1 S2, S3, S6). Two
    // of them have no native entry point at all — nothing has been able to
    // send DT_TEImpact or DT_TEGaussExplosion in this build — so these
    // methods are not a nicer way to reach an effect, they ARE the effect
    // coming back. The third, Dust, is the destination Part 8 names for
    // IEngineEffects.DustEffect.
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    public void Impact(TeFilter filter, float delay, Vector position, Vector normal, int type)
    {
        var state = new TeImpact
        {
            Origin = position,
            Normal = normal,
            Type   = type,
        };

        TempEntType? te = Resolve(state);

        if (te is null)
            return;

        // m_ucFlags is NOT declared, and not because it was forgotten:
        // c_te_impact.cpp declares a fourth recv prop of that name and stock's
        // send table has never had one, so it has always kept the client
        // constructor's zero. The engine matches props by name and a recv prop
        // with no sender is never written. Adding it to the declaration would
        // make this build's wire differ from stock's.
        _engine.TempEntPlayback(te.Handle, state.ToBlob(te), filter, delay);
    }

    /// <inheritdoc/>
    public void GaussExplosion(TeFilter filter, float delay, Vector position, Vector direction,
                               int type)
    {
        // Origin is DT_TEParticleSystem's and DT_TEGaussExplosion does not
        // declare it — but it is at the front of this class's blob, and a
        // GaussExplosion without it happens at the world origin. The
        // declaration inherits, so it is set in the same initialiser as the
        // two the table does declare.
        var state = new TeGaussExplosion
        {
            Origin    = position,
            Type      = type,
            Direction = direction,
        };

        TempEntType? te = Resolve(state);

        if (te is null)
            return;

        _engine.TempEntPlayback(te.Handle, state.ToBlob(te), filter, delay);
    }

    /// <inheritdoc/>
    public void Dust(TeFilter filter, float delay, Vector position, Vector direction,
                     float size, float speed)
    {
        var state = new TeDust
        {
            Origin = position,
            Size   = size,
            Speed  = speed,

            // NORMALISED HERE, which is TE_Dust's own Assert( dir.Length() < 1.01 )
            // turned into the thing it was asserting. Four bits per component over
            // [-1, 1] means a longer vector is CLAMPED by the encoder rather than
            // refused, so it would arrive pointing somewhere else — and an assert is
            // not a check in a release build. shell_worldquery.cpp:2083 already
            // normalises on the way into g_pEffects->Dust for exactly this reason
            // and says so at the call site.
            Direction = direction.Normalized,
        };

        TempEntType? te = Resolve(state);

        if (te is null)
            return;

        _engine.TempEntPlayback(te.Handle, state.ToBlob(te), filter, delay);
    }

    // ------------------------------------------------------------------
    // Physical debris (plan Part 6, lane E). Three angle props out of one
    // QAngle, two symbolic widths and six colour bytes — all of which is
    // said in the DECLARATIONS. Nothing here knows any of it.
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    public void BreakModel(TeFilter filter, float delay, Vector position, QAngle angles,
                           Vector size, Vector velocity, int modelIndex, int randomization,
                           int count, float time, int flags)
    {
        var state = new TeBreakModel
        {
            Origin = position,

            // THREE FLOATS OF BLOB AND THREE PROPS ON THE WIRE, which is not the
            // same statement twice: the declaration says TePropKind.Angle with
            // Elems = 3, so the table carries Rotation[0..2] as three
            // SendPropAngles, and the blob is three consecutive floats either
            // way. Angle is one of the four kinds that STAY EXPLICIT — a Vector
            // member cannot say which of Vector, Angle and QAngles it is, and
            // only the table knows (plan 2B).
            Rotation = angles.AsVector(),

            Size          = size,
            Velocity      = velocity,
            ModelIndex    = modelIndex,
            Randomization = randomization,
            Count         = count,
            Time          = time,
            Flags         = flags,
        };

        TempEntType? type = Resolve(state);

        if (type is null)
            return;

        _engine.TempEntPlayback(type.Handle, state.ToBlob(type), filter, delay);
    }

    /// <inheritdoc/>
    public void DispatchEffect(TeFilter filter, float delay, int effectNameIndex,
                               in TeEffectData data)
    {
        TempEntType? type = Resolve("CTEEffectDispatch");

        if (type is null)
            return;

        Span<byte> blob = stackalloc byte[type.BlobBytes];
        var writer = new TempEntWriter(type, blob);

        writer.Vector("Origin", data.Origin);
        writer.Vector("Start", data.Start);
        writer.Vector("Angles", data.Angles);
        writer.Vector("Normal", data.Normal);

        writer.Int("Flags", data.Flags);
        writer.Float("Magnitude", data.Magnitude);
        writer.Float("Scale", data.Scale);
        writer.Int("AttachmentIndex", data.AttachmentIndex);

        // THE +1 IS THE PROXY, AND IT IS HERE BECAUSE THE PROXY IS GONE.
        // Stock declares m_nSurfaceProp with SendProxy_ShortAddOne
        // (sendproxy.cpp:46), which reads the short and adds one so that -1 —
        // "no surface" — survives an UNSIGNED eight-bit prop as a zero. A send
        // proxy never appears in the table the client pairs against (plan 2B),
        // so the declaration is a plain SendPropInt and this is where the
        // arithmetic went. The client's RecvProxy_ShortSubOne takes it back
        // off, and dropping this line would shift every surface property by one
        // — a different impact sound and different debris, with nothing
        // anywhere saying so.
        writer.Int("SurfaceProp", data.SurfaceProp + 1);

        // AND THIS ONE HAS NO +1, three lines from the one that does. It is the
        // string table index verbatim: the client looks the NAME back up by it
        // (c_te_effect_dispatch.cpp:153) and dispatches on that, so an index off
        // by one plays a different effect rather than none.
        writer.Int("EffectName", effectNameIndex);

        writer.Int("Material", data.Material);
        writer.Int("DamageType", data.DamageType);
        writer.Int("HitBox", data.HitBox);

        // "EntIndex", where stock's prop was called "entindex" and its member
        // m_nEntIndex. SENDINFO_NAME( m_nEntIndex, entindex ) existed because
        // the CLIENT's CEffectData has no such member at all -- it keeps a
        // handle and fills it through RecvProxy_EntIndex, declared under the
        // name "entindex" -- and two hand-written tables had to agree on the
        // string. Neither table is hand-written any more: both halves build
        // their props from TeEffectData, so the name is the member's and the
        // agreement is structural rather than spelled.
        writer.Int("EntIndex", data.EntIndex);

        writer.Int("Color", data.Color);
        writer.Float("Radius", data.Radius);

        writer.Bool("CustomColors", data.CustomColors);
        writer.Vector("Color1", data.Color1);
        writer.Vector("Color2", data.Color2);

        writer.Bool("ControlPoint1", data.ControlPoint1);
        writer.Int("ParticleAttachment", data.ParticleAttachment);
        writer.Vector("ControlPointOffset", data.ControlPointOffset);

        _engine.TempEntPlayback(type.Handle, blob, filter, delay);
    }

    /// <inheritdoc/>
    public void PhysicsProp(TeFilter filter, float delay, int modelIndex, int skin,
                            Vector position, QAngle angles, Vector velocity,
                            int flags, int effects)
    {
        var state = new TePhysicsProp
        {
            Origin     = position,
            Rotation   = angles.AsVector(),
            Velocity   = velocity,
            ModelIndex = modelIndex,

            // SIGNED, and set as a plain int all the same: the sign is the
            // SendProp's (flags of zero, not SPROP_UNSIGNED) and the blob carries
            // the value the proxy would have read.
            Skin = skin,

            Flags   = flags,
            Effects = effects,
        };

        TempEntType? type = Resolve(state);

        if (type is null)
            return;

        _engine.TempEntPlayback(type.Handle, state.ToBlob(type), filter, delay);
    }

    /// <inheritdoc/>
    public void Explosion(TeFilter filter, float delay, Vector position, int modelIndex,
                          float scale, int frameRate, int flags, int radius, int magnitude,
                          Vector? normal = null, byte materialType = (byte)'C')
    {
        var state = new TeExplosion
        {
            // Origin is DT_TEParticleSystem's. It is in this class's BLOB all
            // the same, at the front of it, and a call that left it out would
            // put every explosion at the world origin — which the inheritance
            // is what makes it possible to say here at all.
            Origin     = position,

            ModelIndex = modelIndex,
            Scale      = scale,
            FrameRate  = frameRate,
            Flags      = flags,

            // (0,0,1) for a null normal, which is te_explosion.cpp:126's own
            // substitution: UP, rather than a zero-length normal the client
            // would have to special-case.
            Normal       = normal ?? new Vector(0f, 0f, 1f),

            MaterialType = materialType,
            Radius       = radius,
            Magnitude    = magnitude,
        };

        TempEntType? type = Resolve(state);

        if (type is null)
            return;

        _engine.TempEntPlayback(type.Handle, state.ToBlob(type), filter, delay);
    }

    /// <inheritdoc/>
    public void ShatterSurface(TeFilter filter, float delay, Vector position, QAngle angles,
                               Vector force, Vector forcePosition,
                               float width, float height, float shardSize,
                               ShatterSurfaceType surfaceType,
                               int frontR, int frontG, int frontB,
                               int backR, int backG, int backB)
    {
        var state = new TeShatterSurface
        {
            Origin = position,

            // A QAngle IN THE BLOB AND A COORD VECTOR ON THE WIRE. Stock declares
            // m_vecAngles a CNetworkQAngle and sends it with SendPropVector — no
            // anglemod and no [0,360] — and c_te_glassshatter.cpp:76 receives it
            // with RecvPropVector to match. So this one is INFERRED as a Vector
            // and its neighbours in TeBreakModel are not; the difference is the
            // table's, not the member type's.
            Angles = angles.AsVector(),

            Force       = force,
            ForcePos    = forcePosition,
            Width       = width,
            Height      = height,
            ShardSize   = shardSize,
            SurfaceType = surfaceType,

            // SIX FIELDS, SIX PROPS. Stock spells these with
            // SENDINFO_ARRAYELEM, whose own prop name is the string
            // "m_uchFrontColor[0]" with the brackets in it (dt_send.h:624); this
            // build's wire carries the six member names instead, and the six are
            // still six consecutive one-byte props in this order, which is what
            // TempEntDebrisTests.ColourBytesAreSixConsecutiveProps pins.
            FrontColorR = (byte)frontR,
            FrontColorG = (byte)frontG,
            FrontColorB = (byte)frontB,
            BackColorR  = (byte)backR,
            BackColorG  = (byte)backG,
            BackColorB  = (byte)backB,
        };

        TempEntType? type = Resolve(state);

        if (type is null)
            return;

        _engine.TempEntPlayback(type.Handle, state.ToBlob(type), filter, delay);
    }

    /// <inheritdoc/>
    public void LargeFunnel(TeFilter filter, float delay, Vector position,
                            int modelIndex, int reversed)
    {
        var state = new TeLargeFunnel
        {
            Origin     = position,
            ModelIndex = modelIndex,
            Reversed   = reversed,
        };

        TempEntType? type = Resolve(state);

        if (type is null)
            return;

        _engine.TempEntPlayback(type.Handle, state.ToBlob(type), filter, delay);
    }

    /// <inheritdoc/>
    public void KillPlayerAttachments(TeFilter filter, float delay, int player)
    {
        // The smallest declaration in the inventory: one prop, five bits, and
        // the player is a SLOT rather than an edict index -- which is a fact
        // about the field and therefore lives on TeKillPlayerAttachments.Player
        // rather than here.
        var state = new TeKillPlayerAttachments { Player = player };

        TempEntType? type = Resolve(state);

        if (type is null)
            return;

        _engine.TempEntPlayback(type.Handle, state.ToBlob(type), filter, delay);
    }

    // ------------------------------------------------------------------
    // Lane S5's two (plan Part 8). Neither is a new table: CTEMuzzleFlash is
    // lane F's declaration and DT_TEEffectDispatch is lane S4's. What is new
    // is that MANAGED CODE now reaches both without going through a native
    // forward, which is the whole of Part 8.
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    public void MuzzleFlash(TeFilter filter, float delay, Vector start, QAngle angles,
                            float scale, int type)
    {
        var state = new TeMuzzleFlash
        {
            Origin = start,

            // A QAngle IN THE BLOB AND A COORD VECTOR ON THE WIRE, which is
            // CTEMuzzleFlash's own shape rather than a shortcut: stock declares
            // m_vecAngles a CNetworkQAngle and sends it with SendPropVector — no
            // anglemod and no [0,360] — and c_te_muzzleflash.cpp receives it with
            // RecvPropVector to match. Three floats either way; the encoding is the
            // declaration's business (plan 2B), and this one is INFERRED as a
            // Vector where DT_TEProjectedDecal's facing stays an explicit QAngles.
            Angles = angles.AsVector(),

            Scale = scale,
            Type  = type,
        };

        TempEntType? te = Resolve(state);

        if (te is null)
            return;

        _engine.TempEntPlayback(te.Handle, state.ToBlob(te), filter, delay);
    }

    /// <inheritdoc/>
    public void DispatchEffect(TeFilter filter, float delay, string effectName,
                               in TeEffectData data)
    {
        // ONE LINE, AND IT IS THE WHOLE OF THE OVERLOAD. The numbering is the
        // engine's: EffectDispatchIndex is g_pStringTableEffectDispatch->
        // AddString and nothing else, so this cannot drift from what the client
        // reads the name back out of. A managed dictionary of names here would
        // be a second numbering of one table, and two numberings that disagree
        // play the WRONG effect rather than none.
        //
        // Resolved EVERY CALL rather than cached. AddString on a name already
        // present answers the index it already has, so the cost is a hash of a
        // short string; a cache would have to be invalidated at level change,
        // because the table is rebuilt per level, and a stale index is exactly
        // the failure this whole path is written to avoid.
        DispatchEffect(filter, delay, _engine.EffectDispatchIndex(effectName), in data);
    }

    /// <summary>
    /// The registered type for a declaration, found from the declaration itself.
    ///
    /// <para>
    /// A call site used to name its class as a string — <c>Resolve("CTESparks")</c>
    /// — which is the same class name the <c>[TempEntity]</c> on the declaration
    /// already carries, typed a second time and checked by nothing. Asking the
    /// state removes the second copy: rename the attribute and every caller
    /// follows, misspell it and it does not compile.
    /// </para>
    /// </summary>
    private TempEntType? Resolve(TempEntState state)
    {
        Type declared = state.GetType();
        var attr = declared.GetCustomAttribute<TempEntityAttribute>();

        if (attr is null)
        {
            _log.Warning($"[SourceSharp] {state.GetType().Name} has no [TempEntity] on it, so " +
                        "there is no class name to resolve it by; nothing sent\n");
            return null;
        }

        return Resolve(TempEntityAttribute.ClassNameOf(declared));
    }

    private TempEntType? Resolve(string className)
    {
        TempEntType? type = _registry.Find(className);

        if (type is { Registered: true })
            return type;

        if (_complained.Add(className))
        {
            _log.Warning(
                $"[SourceSharp] temp entity '{className}' is not registered on this side; " +
                "every call to it does nothing. The registration line in the console says " +
                "why it was refused.\n");
        }

        return null;
    }
}
