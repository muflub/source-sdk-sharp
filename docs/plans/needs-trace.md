# needs-trace — promotions requested by the trace cluster (BoneSetupTrace.cs)

Ownership: TraceAgent. For the integrator; nothing here is edited in the
shared files (StudioHdr.cs / StudioHdrBuilder.cs are Main's right now).

## Types defined locally, pending a shared home

Defined in `src/shared/managed/Bones/BoneSetupTrace.cs` because neither
BoneSetupMath.cs nor StudioHdr.cs carries them. Promote when a second
consumer appears (physics tracelist is the expected one):

- `BoneTrace` — stock `trace_t`/`CBaseTrace` fields this cluster writes
  (fraction, startsolid, allsolid, endpos, startpos, plane flattened to
  normal/dist/type, hitgroup, hitbox, contents, physicsbone, surfaceFlags,
  surfaceProps, surfaceName). Mutable class: stock passes `trace_t&` and the
  sweep copies whole traces out of a local.
- `BoneRay` — stock `Ray_t` (cmodel.h:61-99): start/delta/startOffset/
  extents/isRay/isSwept, `FromPoints` = `Init(start,end)`, `FromBox` =
  `Init(start,end,mins,maxs)` incl. the 1e-6 extents test and negated
  startOffset.
- `SurfacePropReader` delegate — the injected answer for stock's
  `pProps->GetSurfaceIndex(pBone->pszSurfaceProp())`.
- `BoneSetupTrace.ContentsSolid` (bspflags.h:36, =1), `ContentsHitbox`
  (bspflags.h:76, 0x40000000), `SurfHitbox` (0x8000).

## Private collision mirrors, pending promotion

Ported into BoneSetupTrace.cs as private statics (expression order kept)
because no managed assembly owns them yet; each cites its stock original in
`src/public/collisionutils.cpp` / `mathlib.h`:

- `BoxTraceInfo` + `IntersectRayWithBoxCore` (collisionutils.cpp:1130-1202,
  the six-face slab walk; faces in min,min,min,max,max,max order, negative
  faces measure `-start[i]+mins[i]` with delta SUBTRACTED).
- `IntersectRayWithBox(start,delta,min,max,tol,pTrace)` (1208-1259,
  CBaseTrace arm; clean-entry + startsolid arms).
- `IntersectRayWithOBB(start,delta,matrix,min,max,tol,pTrace)` (1301-1393,
  CBaseTrace arm; SAT box axes + cross axes, slab clip, fraction doubles,
  flSign column plane, type 3).
- `IntersectRayWithOBB(Ray_t,matrix,min,max,tol,pTrace)` (1465-1685, Ray_t
  dispatch; swept form = explicit-GJK arm: bounding-sphere reject, 9 OBB +
  6 edge planes with support maps, enter/leave clip, two hit arms,
  MatrixITransformPlane fixup).
- `IsRayIntersectingSphere` (375-411).
- `ClearTrace`, `MatrixITransformPlane` (mathlib).

These are mathlib/collisionutils members, not bone_setup members: they
belong with a future managed mathlib (BoneSetupMath's home), not in Bones.

## Reader gaps (StudioHdr.cs) — reads done via private fallback

`MstudioBone` exposes neither field; BoneSetupTrace reads them through
`StudioRecord.RawImage/RawAddress` + the compiled-layout offsets (dump.txt,
sizeof 216):

- `contents` @180 (stock `pBone->contents` gate, bone_setup.cpp:5277/5342).
- `physicsbone` @172 (stock `tr.physicsbone = pBone->physicsbone`,
  5305/5407).

Suggested additions (HdrOffsetAudit's pass is the natural owner):
`public int Contents => I32(180); public int PhysicsBone => I32(172);` on
`MstudioBone`, mirroring `SurfacePropIndex => I32(176)`.

## Fixture-builder gap (StudioHdrBuilder.cs) — harness works around it

`mstudiobbox_t` records are emitted 64 bytes, not 68: the
`szhitboxnameindex`@32 slot is skipped (`QueueString` defers the patch and
consumes no stream bytes), so a second box in a set reads shifted by -4
(bone reads the old group; group reads float garbage), and the named path is
worse — `QueueString(at, 0, h.Name)` patches at+0 and clobbers bone@0.
Fix for Main (who owns the file now): after `WVec(h.Max)`,
`W32(h.Name is not null ? <queued> : 0)` at @32, keep `W32(h.SurfaceProp)`
at @36 (the builder's unused[0] convention), `Pad(4*7)`, and assert
`Mark() == at + 68` like the bone/ikrule records do.
`harness-trace/Program.cs` relayouts the boxes to true 68-byte records at
the image tail (`RelayoutHitboxes`) until that lands.
