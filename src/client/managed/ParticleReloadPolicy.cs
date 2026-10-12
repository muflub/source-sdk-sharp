namespace SourceSharp.Client;

/// <summary>
/// Whether a level's LevelInitPreEntity must re-read every particle definition
/// file (plan_map_load P8).
///
/// <para>
/// Stock re-read the whole manifest at every level ("reload all the particles
/// regardless"), because a map's own particle manifest replaces definitions by
/// name and nothing else puts the stock ones back. Measured on a remote client
/// at 178bfc1: 1.0–1.5 s of DMX parsing per hop. The definitions themselves
/// survive a level change; only their precache does not. So a re-read is
/// needed only on the first level, when nothing was read with precache yet, and
/// after a level that loaded map particle files. Every other level re-precaches
/// what is already defined.
/// </para>
/// </summary>
public sealed class ParticleReloadPolicy
{
    /// <param name="previousMapParticleFiles">
    /// The map-specific particle files the previous level loaded, or -1 when
    /// there was no previous level.
    /// </param>
    public bool NeedsFullReload(int previousMapParticleFiles) => previousMapParticleFiles != 0;
}
