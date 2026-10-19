namespace Debug.Api.Client.Interfaces;

/// <summary>
/// One CNewParticleEffect instance, as <c>/client/{slot}/particles</c> reports
/// it. <paramref name="Owner"/> is the owning entity's handle, null for a
/// one-shot at a world point. <paramref name="Attachment"/> is how it is kept
/// in place: "follow" (its <paramref name="FollowPoints"/> are kept at the
/// owner every frame), "world" (a one-shot, placed once), or "released" (its
/// owner let it go; it burns out where it is).
/// </summary>
public sealed record DebugClientParticleSystem(
    string Name, uint? Owner, string Attachment, IReadOnlyList<int> FollowPoints, float Age, int Particles,
    DebugClientPoint ControlPoint0, DebugClientPoint ControlPoint1, DebugClientPoint SortOrigin);

/// <summary>A point in world space.</summary>
public readonly record struct DebugClientPoint(float X, float Y, float Z);

/// <summary>
/// The client's particle systems (plan_client_novalve §4.4): every instance
/// the particles shell started and the library still holds, and how many
/// times the definitions were read. <paramref name="DefinitionReads"/> is -1
/// when the read is not wired.
/// </summary>
/// <param name="QueryUnanswered">
/// The particle query's calls (IParticleSystemQuery, the library's callbacks)
/// that found no managed answer, by method name. The collision-group names
/// read at Init, before the managed runtime exists, are the expected ones.
/// </param>
public sealed record DebugClientParticles(int DefinitionReads, int WithParticles, IReadOnlyList<DebugClientParticleSystem> Systems,
                                          IReadOnlyList<DebugCount> QueryUnanswered);

/// <summary>The client's particle systems (plan_client_novalve §4.4). Game thread.</summary>
public interface IDebugClientParticles
{
    DebugClientParticles Read();
}
