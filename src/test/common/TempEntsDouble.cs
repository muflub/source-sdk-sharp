using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Tests;

/// <summary>
/// A no-op <see cref="ITempEnts"/> for tests to derive from, overriding only
/// the one or two methods they actually assert on.
///
/// <para>
/// WHY THIS EXISTS. <c>ITempEnts</c> grows one lane at a time — it is stock's
/// <c>ITempEntsSystem</c>, forty-four effects wide when the port lands — and
/// every lane that wrote its own <c>: ITempEnts</c> double had to spell out
/// every method the OTHER lanes had added. Three doubles were written that way
/// and all three stopped compiling the moment two of those lanes met on the
/// integration branch, in a file neither lane had touched.
/// </para>
///
/// <para>
/// NOT DEFAULT INTERFACE METHODS, which would do the same job in one place and
/// is the wrong tool here. A default no-op on <c>ITempEnts</c> itself would
/// also apply to <c>SourceSharp.Server.TempEnts</c>: a real effect whose
/// implementation was forgotten would compile, register, be called, and do
/// nothing, with no diagnostic anywhere. That is precisely the quiet lapse this
/// port's behaviour ledger exists to prevent, so the convenience stays on the
/// test side of the line where a missing method is only ever a missing
/// assertion.
/// </para>
/// </summary>
internal abstract class TempEntsDouble : ITempEnts
{
    public virtual void Sparks(TeFilter filter, float delay, Vector position,
                               int magnitude, int trailLength, Vector? direction) { }

    public virtual void MetalSparks(TeFilter filter, float delay, Vector position, Vector direction) { }
    public virtual void BloodStream(TeFilter filter, float delay, Vector origin, Vector direction, Color32 color, int amount) { }
    public virtual void Smoke(TeFilter filter, float delay, Vector origin, int modelIndex, float scale, int frameRate) { }

    public virtual void EnergySplash(TeFilter filter, float delay, Vector position,
                                     Vector direction, bool explosive) { }

    public virtual void BSPDecal(TeFilter filter, float delay, Vector position,
                                 int entity, int index) { }

    public virtual void ProjectDecal(TeFilter filter, float delay, Vector position,
                                     QAngle angles, float distance, int index) { }

    public virtual void Decal(TeFilter filter, float delay, Vector position, Vector start,
                              int entity, int hitbox, int index) { }

    public virtual void FootprintDecal(TeFilter filter, float delay, Vector origin,
                                       Vector right, int entity, int index,
                                       byte materialType) { }

    public virtual void PlayerDecal(TeFilter filter, float delay, Vector position,
                                    int player, int entity) { }

    public virtual void WorldDecal(TeFilter filter, float delay, Vector position, int index) { }

    public virtual void Fizz(TeFilter filter, float delay, int entIndex,
                             int modelIndex, int density, int current) { }

    public virtual void Bubbles(TeFilter filter, float delay, Vector mins, Vector maxs, float height,
                                int modelIndex, int count, float speed) { }

    public virtual void BubbleTrail(TeFilter filter, float delay, Vector from, Vector to, float waterZ,
                                    int modelIndex, int count, float speed) { }

    public virtual void Impact(TeFilter filter, float delay, Vector position,
                               Vector normal, int type) { }

    public virtual void GaussExplosion(TeFilter filter, float delay, Vector position,
                                       Vector direction, int type) { }

    public virtual void Dust(TeFilter filter, float delay, Vector position, Vector direction,
                             float size, float speed) { }

    public virtual void BreakModel(TeFilter filter, float delay, Vector position, QAngle angles,
                                   Vector size, Vector velocity, int modelIndex,
                                   int randomization, int count, float time, int flags) { }

    public virtual void PhysicsProp(TeFilter filter, float delay, int modelIndex, int skin,
                                    Vector position, QAngle angles, Vector velocity,
                                    int flags, int effects) { }

    public virtual void Explosion(TeFilter filter, float delay, Vector position, int modelIndex,
                                  float scale, int frameRate, int flags, int radius,
                                  int magnitude, Vector? normal = null,
                                  byte materialType = (byte)'C') { }

    public virtual void ShatterSurface(TeFilter filter, float delay, Vector position,
                                       QAngle angles, Vector force, Vector forcePosition,
                                       float width, float height, float shardSize,
                                       ShatterSurfaceType surfaceType,
                                       int frontR, int frontG, int frontB,
                                       int backR, int backG, int backB) { }

    public virtual void LargeFunnel(TeFilter filter, float delay, Vector position,
                                    int modelIndex, int reversed) { }

    public virtual void KillPlayerAttachments(TeFilter filter, float delay, int player) { }
    public virtual void DispatchEffect(TeFilter filter, float delay, int effectNameIndex,
                                       in TeEffectData data) { }

    public virtual void MuzzleFlash(TeFilter filter, float delay, Vector start,
                                    QAngle angles, float scale, int type) { }

    public virtual void DispatchEffect(TeFilter filter, float delay, string effectName,
                                       in TeEffectData data) { }
}
