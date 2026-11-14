using SourceSharp.Host.Contracts;

namespace TF2.Descent.Tests.Layout;

/// <summary>
/// A synthetic room catalogue that mirrors plan_descent_mine_levels section 5: the
/// seven connectors, five chambers, six halls and shafts and the role rooms, each
/// with several door variants, spelled in the room-name codec (E8). Jump flags
/// follow section 4.3's features (a mezzanine is S2, a rocket shelf B1, a catwalk
/// ring B2, a roof gallery B3). Depth ranges stand in for the fifteen libraries.
/// M18 ends every branch in a destination, so the chambers and halls each have a
/// one-door variant (pillar_e, pit_d, store_a/c, stope_d, gallery_e, chimney_a,
/// cavern_e, pool_d, crypt_d); stub_b, a stub with no purpose tag, is the hallway
/// a branch must never end in. M21's counted fewests need each counted family to fit
/// more than one footprint, so the chasm has a bend (chasm_c) beside its straights.
/// </summary>
internal static class LayoutCatalogue
{
    private static RoomInfo R(string name, int min, int max, int weight = 1, params string[] tags) => new(name, tags, weight, min, max);

    public static IReadOnlyList<RoomInfo> Rooms() =>
    [
        // Connectors (low).
        R("drift_a-ew-l", 1, 14, 2, "corridor"),
        R("drift_b-ns-l", 1, 14, 1, "corridor"),
        R("bend_a-ne-l", 1, 14, 2, "corridor"),
        R("bend_b-sw-l", 1, 14, 1, "corridor"),
        R("tee_a-esw-l", 1, 14, 1, "junction"),
        R("tee_b-new-l", 1, 14, 1, "junction"),
        R("cross_a-nesw-l", 1, 14, 1, "junction"),
        R("dogleg_a-ew-l", 1, 14, 1, "corridor"),
        R("dogleg_b-ns-l", 6, 14, 1, "corridor"),
        R("stub_a-e-l", 1, 14, 1, "shrine"),
        R("stub_b-n-l", 1, 14, 1),
        R("widedrift_a-ew-l", 1, 14, 1, "corridor"),
        R("widedrift_b-ne-l", 2, 14, 1, "corridor"),
        // Low crypts and crawls: low combat rooms (levels 9 and 14).
        R("crypt_a-ew-l", 6, 14),
        R("crypt_b-ne-l", 6, 14),
        R("crypt_c-nesw-l", 6, 14),
        R("crypt_d-e-l", 6, 14),
        // Chambers (512).
        R("pillar_a-ew-c", 1, 14, 2),
        R("pillar_b-nesw-c", 1, 14),
        R("pillar_c-nes-c", 1, 14),
        R("pillar_d-ne-c", 1, 14),
        R("pillar_e-e-c", 1, 14),
        R("split_a-ew-c-j.s2", 1, 14),
        R("split_b-nes-c-j.s2", 1, 14),
        R("split_c-ne-c-j.s2.b1", 1, 14),
        R("pit_a-nesw-c-j.s2.s3", 1, 14),
        R("pit_b-ew-c-j.s2.s3", 1, 14),
        R("pit_c-ne-c-j.s2", 1, 14),
        R("pit_d-e-c-j.s2.s3", 1, 14),
        R("store_a-e-c", 1, 14),
        R("store_b-ew-c-j.b1", 1, 14),
        R("store_c-e-c-j.b1", 1, 14),
        R("yard_a-esw-c", 1, 14),
        R("yard_b-nesw-c-j.b1", 1, 14),
        // Halls and shafts (1024 and up).
        R("stope_a-ew-h-j.s3.b1.b2", 1, 14),
        R("stope_b-nes-h-j.s3.b1", 1, 14),
        R("stope_c-ne-h-j.b1.b2", 1, 14),
        R("stope_d-e-h-j.s3.b1", 1, 14),
        R("gallery_a-nesw-h-j.b1.b2", 1, 14),
        R("gallery_b-ew-h-j.b1.b2", 1, 14),
        R("gallery_c-esw-h-j.b2", 1, 14),
        R("gallery_d-ne-h-j.b1", 1, 14),
        R("gallery_e-e-h-j.b1.b2", 1, 14),
        R("chasm_a-ew-h-j.s2.s3", 1, 14),
        R("chasm_b-ns-s-j.s3.b3", 1, 14),
        R("chasm_c-ne-h-j.s2.s3", 1, 14),
        R("chimney_a-e-s-j.b2.b3", 1, 14),
        R("chimney_b-ew-s-j.b2.b3", 1, 14),
        R("chimney_c-ne-s-j.b3", 1, 14),
        R("cavern_a-nesw-h-j.s2.b1", 6, 14),
        R("cavern_b-ew-s-j.b3", 6, 14),
        R("cavern_c-nes-h-j.s3", 6, 14),
        R("cavern_d-ne-s-j.b2.b3", 6, 14),
        R("cavern_e-e-h-j.s2.b1", 6, 14),
        R("pool_a-ew-h-j.s2", 8, 14),
        R("pool_b-nesw-c-j.s2.s3", 8, 14),
        R("pool_c-ne-h-j.s3.b1", 8, 14),
        R("pool_d-e-c-j.s2", 8, 14),
        // Treasure rooms.
        R("treasure_a-e-c", 1, 14, 1, "treasure"),
        R("treasure_b-n-h-j.b1", 1, 14, 1, "treasure"),
        // Arrivals: cage stations, the act II and III ways in, the camps.
        R("arrival_cage-e-c", 1, 4, 1, "start"),
        R("arrival_station-ew-c", 1, 4, 1, "start"),
        R("arrival_rubble-e-c", 6, 9, 1, "start"),
        R("arrival_ramp-n-h", 7, 9, 1, "start"),
        R("arrival_chute-e-h", 11, 14, 1, "start"),
        R("camp_a-e-c", 6, 6, 1, "start"),
        R("camp_b-ew-c", 11, 11, 1, "start"),
        // Exits.
        R("exit_cage-w-c", 1, 4, 1, "stairs"),
        R("exit_ramp-s-h", 6, 9, 1, "stairs"),
        R("exit_sump-sw-c", 8, 9, 1, "stairs"),
        R("exit_chute-w-c", 11, 14, 1, "stairs"),
        // Boss levels: their arrival, antechambers, arena and way down.
        R("arrival_lift-e-c", 5, 5, 1, "start"),
        R("arrival_bore-e-h", 10, 10, 1, "start"),
        R("arrival_slide-e-s", 15, 15, 1, "start"),
        R("antechamber_a-ew-c", 5, 15),
        R("antechamber_b-ew-h-j.s2", 5, 15),
        R("antechamber_c-nesw-c", 5, 15),
        R("arena_magazine-ew-h-j.s2.b1", 5, 5, 1, "boss_arena"),
        R("arena_bore-ew-h-j.s2.s3.b2", 10, 10, 1, "boss_arena"),
        R("arena_pit-w-s-j.s2.s3.b1.b2", 15, 15, 1, "boss_arena"),
        R("exit_hole-w-c", 5, 5, 1, "stairs"),
        R("exit_borehole-w-h", 10, 10, 1, "stairs"),
    ];
}
