/*
 * The shapes the development API answers with.
 *
 * Written from the emitters in DebugApiServer rather than inferred from a sample
 * response, so an optional field is optional because the server can leave it out
 * — not because the one map it was tried against happened not to have it.
 *
 * These are the contract. When an endpoint changes, this file is the thing that
 * makes every call site that cared fail to compile.
 */

export type Vec3 = [number, number, number];

/** Whether what is loaded is still what is on disk. */
export type Freshness = 'unknown' | 'current' | 'stale';

/**
 * One half of the build the game is running.
 *
 * `known` is its own field rather than being inferred from an empty hash,
 * because "nothing stamped this" and "a stamp of nothing" are different states
 * and a page that cannot tell them apart renders the first as the second.
 */
export interface BuildHalf {
  known: boolean;

  /** Eight hex characters over the bytes that were LOADED, not over a commit. */
  hash: string;

  /** When those bytes were written, epoch milliseconds. 0 when unknown. */
  at: number;

  /** How many files went into the hash. */
  parts: number;

  /** The branch it was built on, or "(detached)". Empty when git said nothing. */
  branch: string;
  commit: string;

  /**
   * Whether the tree had uncommitted work in it.
   *
   * THREE-VALUED. null means git could not be asked, and must not render the
   * way false does — a build containing uncommitted work presenting itself as a
   * clean named commit is the exact failure this whole stamp is shaped around.
   */
  dirty: boolean | null;

  /** The three above, already rendered: "branch@commit+dirty". */
  source: string;

  freshness: Freshness;
  path: string;
}

/**
 * TWO HALVES, NOT ONE. cs_sv_reload replaces the managed half without
 * restarting the process, so they legitimately diverge — and seeing that they
 * have is more useful than a single number that would have to hide it.
 */
export interface Build {
  native: BuildHalf;
  managed: BuildHalf;
}

export interface Status {
  ok: boolean;
  pid: number;
  address: string;
  time: number;
  ticking?: boolean;
  map?: string;
  ticks?: number;
  error?: string;

  /** Which build this game is running. Absent from a reply an older game gave. */
  build?: Build;
}

export interface LogLine {
  seq: number;
  warning: boolean;
  text: string;

  /**
   * When the game said it, in milliseconds since the epoch.
   *
   * Absent on a line this page wrote about itself, which has no game time to
   * report — see pushLocal.
   */
  at?: number;
}

export interface LogPage {
  next: number;
  lines: LogLine[];

  /** Only on /console: where it is reading from, and whether it found anything. */
  path?: string;
  following?: boolean;
}

/**
 * Which registry a row came from.
 *
 * "managed" is a convar or command this mod declared: a C# class owns it, its
 * type was stated, and the value on screen is that class's own. "native" is one
 * the engine or a game DLL declared and this tool only reads — no type was ever
 * stated for it, and nothing here can do more than the console can.
 *
 * Worth carrying on every row rather than inferring from a filter, because a
 * merged listing shows both at once and the difference decides what an editor
 * is even allowed to offer.
 */
export type ConSource = 'managed' | 'native';

export interface ConVar {
  name: string;
  value: string;
  default: string;
  help: string;

  /** "unknown" for a native convar: nobody ever declared a type for one. */
  type: 'string' | 'int' | 'float' | 'bool' | 'unknown';
  flags: string;

  /** False when the engine refused it: it still reads, but writes go nowhere. */
  registered: boolean;

  /**
   * The bounds the engine clamps to, or null where there are none.
   *
   * null rather than an absent field or a sentinel: zero is a perfectly ordinary
   * minimum, and any number picked to mean "unbounded" is one some convar
   * legitimately clamps to.
   */
  min: number | null;
  max: number | null;

  source: ConSource;

  /**
   * Where it came from: the C# class for one of ours, the DLL that registered
   * it for one of the engine's — as far as the flags say, which is all the
   * engine records about a convar's origin.
   */
  declaredBy: string;
}

export interface ConCommand {
  name: string;
  help: string;

  /** The C# class for one of ours; the DLL that registered it for the engine's. */
  declaredBy: string;

  /** Empty for one of ours, which are registered with no flags at all. */
  flags: string;

  /**
   * False when the engine would not take the name — a collision with one it
   * already had. The class exists and was built; nothing a player types reaches
   * it. Always true for a native row, which is in the engine's table by
   * definition.
   */
  registered: boolean;

  source: ConSource;
}

/** Which of the container's two keys a service is asked for by. */
export type ServiceLookup = 'type' | 'name';

/**
 * One row of the server's service container.
 *
 * What an entity, a console command or another service may ask for in its own
 * constructor. Before this view the only answer was to read ServerModule's
 * Initialize, which is the kind of question a debugging tool should not make
 * somebody read source to answer.
 */
export interface Service {
  /** The contract's type name, or the console name — see `lookedUpBy`. */
  key: string;

  /**
   * `type` for a contract written in a constructor and checked by the compiler,
   * `name` for the string a `[Named]` parameter carries.
   */
  lookedUpBy: ServiceLookup;

  /**
   * What sort of thing it is: `instance`, `lazy`, `self` (the container), or
   * `convar` / `command` for the console's own.
   */
  kind: string;

  /**
   * The class that actually answers. A contract says what may be called; only
   * this says WHICH object arrives, and that is the half that is wrong when an
   * entity consults something that is not the live system.
   */
  implementation: string;

  /**
   * False only for a lazy registration nothing has asked for yet. Not a fault —
   * a service nothing has needed is one nothing has paid for.
   */
  built: boolean;
}

export interface ScriptedField {
  name: string;
  type: string;
}

export interface ScriptedClass {
  className: string;
  kind: string;
  declaredBy: string;
  liveCount: number;
  inputs: string[];
  outputs: string[];
  vars: ScriptedField[];
}

export interface ScriptedVar extends ScriptedField {
  value: string;
}

export interface ScriptedOutput {
  name: string;
  fireCount: number;

  /** Null when it has never fired — which is not the same as having fired with nothing. */
  lastValue: string | null;
}

export interface ScriptedInstance {
  className: string;
  handle: number;
  index: number;
  valid: boolean;
  origin: Vec3;
  health: number;
  spawnFlags: number;
  vars: ScriptedVar[];
  outputs: ScriptedOutput[];
}

export interface KeyValue {
  key: string;
  value: string;
}

export interface Connection {
  output: string;
  target: string;
  input: string;
  param: string;
  delay: number;
  times: number;

  /** Set by the editor on a connection it made, so it can be drawn as new. */
  added?: boolean;
}

/** An entity as the compiled map declares it. */
export interface MapEntity {
  id: number;
  classname: string;
  targetname: string;
  model: string;

  /** The index into the BSP's model list for a brush entity, or -1. */
  brushModel: number;
  origin: Vec3;
  angles: Vec3;
  keyvalues: KeyValue[];
  connections: Connection[];
}

/** An entity the engine is holding right now. */
export interface LiveEntity {
  handle: number;
  index: number;
  classname: string;
  health: number;
  origin: Vec3;
  angles: Vec3;
}

/*
 * The map file, lump by lump.
 *
 * A separate shape from MapInfo on purpose: that one describes a map the way the
 * entity debugger needs it, and this one describes the file. The overlap is the
 * name and the version, and joining them would mean every view of one carrying
 * the other's fields around.
 */

/** One lump's place in the file, and what is known about its shape. */
export interface MapLump {
  index: number;
  name: string;
  offset: number;
  length: number;
  version: number;

  /** Non-zero only on a compressed lump, which nothing here decodes. */
  uncompressedSize: number;

  /** Bytes per record, or 0 for a lump that is not an array of one. */
  stride: number;

  /** How many records that makes, or -1 when the lump has no fixed shape. */
  records: number;

  /** Why the lump matters, for the ones where knowing changes what you do. */
  note: string;
}

/** A sub-lump of the game lump. Static props and detail props live in these. */
export interface MapGameLump {
  id: string;
  flags: number;
  version: number;
  offset: number;
  length: number;
}

export interface MapClassCount {
  classname: string;
  count: number;
}

export interface MapMaterial {
  index: number;
  name: string;
  width: number;
  height: number;
  viewWidth: number;
  viewHeight: number;

  /** How many world faces are textured with it. Zero means nothing uses it. */
  faces: number;

  /** World units squared of surface built out of it, added up across those faces. */
  area: number;

  /** Lightmap samples spent lighting that surface, styles and bump basis included. */
  luxels: number;

  reflectivity: Vec3;
}

export interface MapArea {
  index: number;
  firstPortal: number;
  portals: number;
}

export interface MapAreaPortal {
  index: number;

  /** What an entity's "portalnumber" key binds to. */
  key: number;
  otherArea: number;
  plane: number;
  firstVertex: number;
  vertices: number;
}

export interface MapWater {
  index: number;
  surfaceZ: number;
  minZ: number;
  texinfo: number;
  material: string;
}

export interface MapDetailModel {
  index: number;
  name: string;
}

export interface MapProp {
  index: number;
  model: string;
  origin: Vec3;
  angles: Vec3;
  skin: number;
  fadeMin: number;
  fadeMax: number;
  solid: number;
  flags: number;
  firstLeaf: number;
  leafCount: number;
}

export interface MapCubemap {
  origin: Vec3;
  size: number;
}

export interface MapWorldLight {
  index: number;
  origin: Vec3;
  intensity: Vec3;
  normal: Vec3;
  type: string;
  style: number;
  radius: number;

  /** The entity the light came from, as vrad numbered them. */
  owner: number;

  /** Which of the two lighting sets it belongs to. A map can be missing either. */
  hdr: boolean;
}

export interface MapOverlay {
  id: number;
  texinfo: number;
  material: string;
  origin: Vec3;
  faces: number;
}

export interface MapDisplacement {
  index: number;
  start: Vec3;
  power: number;
  face: number;
  contents: number;
  vertices: number;
}

export interface MapPakEntry {
  name: string;
  bytes: number;
  compressed: number;
}

export interface MapBrushModel {
  mins: Vec3;
  maxs: Vec3;
  origin: Vec3;
}

/** Everything read out of one map file. */
export interface MapFile {
  map: string;
  path: string;
  bytes: number;
  ident: string;
  version: number;
  revision: number;

  /** Bytes no lump claims: alignment, and whatever a rewritten lump left behind. */
  unaccounted: number;

  /** Zero means the map was compiled without vis and draws everything at once. */
  visClusters: number;

  /**
   * How many clusters an average one can see, and the worst.
   *
   * Everything visible is drawn, so this is the size of a frame's work in the
   * unit the compiler thinks in — and it is nowhere in the file, because the
   * lump holds the visible sets rather than their sizes. -1 when there are none.
   */
  visAverage: number;
  visMax: number;

  occluders: number;

  /** What the level was compiled with, already spelled out. Empty for nothing. */
  levelFlags: string;
  entityCount: number;
  connectionCount: number;

  /** What could not be decoded, rather than silently missing rows. */
  warnings: string[];

  lumps: MapLump[];
  gameLumps: MapGameLump[];
  classes: MapClassCount[];
  materials: MapMaterial[];

  /** The raw name table. Longer than the material list when a name is unused. */
  materialNames: string[];
  propModels: string[];
  props: MapProp[];
  cubemaps: MapCubemap[];
  worldlights: MapWorldLight[];
  overlays: MapOverlay[];
  displacements: MapDisplacement[];
  pak: MapPakEntry[];
  models: MapBrushModel[];
  areas: MapArea[];
  areaportals: MapAreaPortal[];
  water: MapWater[];
  detailModels: MapDetailModel[];

  error?: string;
}

/*
 * The lumps read one at a time.
 *
 * Not part of the report because they are the big ones: a medium map has six
 * thousand faces and sixteen thousand brush sides, and carrying those with every
 * read would mean paying megabytes to look at the pakfile. Asked for only when
 * somebody opens one.
 */

export interface MapFace {
  index: number;
  material: string;

  /** The surface flags, as words. Empty when it has none. */
  surface: string;
  area: number;
  lightmapWidth: number;
  lightmapHeight: number;

  /** Lightmap samples, counting light styles and the bump basis. */
  luxels: number;
  styles: number;
  texinfo: number;
  plane: number;
  side: boolean;
  onNode: boolean;
  firstEdge: number;
  edges: number;
  dispInfo: number;
  origFace: number;
  primitives: number;
}

export interface MapTexInfo {
  index: number;
  material: string;
  surface: string;

  /** World units per luxel. Sixteen is the default; smaller costs more lightmap. */
  lightmapScale: number;
  texdata: number;
}

export interface MapLeaf {
  index: number;

  /** -1 for a solid leaf, which is nowhere the player can be. */
  cluster: number;
  area: number;
  contents: string;
  firstFace: number;
  faces: number;
  firstBrush: number;
  brushes: number;
  water: number;
  mins: Vec3;
  maxs: Vec3;
}

export interface MapBrush {
  index: number;
  contents: string;
  firstSide: number;
  sides: number;
}

export interface MapBrushSide {
  index: number;
  material: string;
  plane: number;
  texinfo: number;
  dispInfo: number;
  bevel: boolean;
}

/** One lump, decoded on request. Only one of the lists is ever filled. */
export interface MapLumpDetail {
  map: string;
  lump: number;
  name: string;

  /** False for a lump nothing decodes — which is not the same as an empty one. */
  known: boolean;

  faces: MapFace[];
  texinfos: MapTexInfo[];
  leaves: MapLeaf[];
  brushes: MapBrush[];
  brushsides: MapBrushSide[];

  error?: string;
}

export interface MapInfo {
  current: string;
  directory: string;
  available: string[];
  loaded: boolean;
  map?: string;
  path?: string;
  version?: number;
  entityCount?: number;
  brushModelCount?: number;
  connectionCount?: number;
  error?: string;
}

export interface BrushModel {
  mins: Vec3;
  maxs: Vec3;
  origin: Vec3;
}

export interface MapGeometry {
  map: string;
  mins: Vec3;
  maxs: Vec3;
  vertices: number[];

  /*
   * Faces as a start and a length into one index array rather than as nested
   * arrays. A map is thousands of faces and the brackets alone would be a fifth
   * of the response.
   */
  faceStarts: number[];
  faceLengths: number[];
  faceIndices: number[];
  models: BrushModel[];
  error?: string;
}

export type EntityEventKind = 'spawned' | 'removed' | 'input' | 'output' | 'level';

export interface EntityEvent {
  seq: number;
  time: number;
  kind: EntityEventKind;
  classname: string;
  name: string;
  detail: string;
  index: number;
  handle: number;
}

export interface EntityEventPage {
  next: number;

  /** Whether the sampler is running. It only does while something is reading. */
  sampling: boolean;
  samplePeriod: number;
  events: EntityEvent[];
  count: number;
}

/** Anything that ran a console command reports where its output starts. */
export interface CommandResult {
  ok?: boolean;
  error?: string;
  command?: string;
  logFrom?: number;
}

export interface SpawnedEntity {
  classname: string;
  name: string;
  index: number;
  created: boolean;
  error: string;
}

export interface SpawnResult extends CommandResult {
  created?: number;
  requested?: number;
  entities?: SpawnedEntity[];
}

export interface SaveResult {
  ok?: boolean;
  error?: string;
  map?: string;
  path?: string;
  entityCount?: number;
  bytes?: number;
  backup?: string;
  overwrote?: boolean;
}

export interface PlayerAim {
  player?: number;
  eye?: Vec3;
  aim?: Vec3;
  hit?: boolean;
  hitClass?: string;
  error?: string;
}

/** One game, as the proxy in front of several of them describes it. */
export interface Instance {
  pid: number;
  ok: boolean;
  map: string;
  address: string;
}

/*
 * The networked schema, and one entity's bytes read back through it.
 *
 * These describe what replaces cs_sv_dump_nettable. Today a networked class is
 * legible from the console — every prop's name, offset, bit count and type —
 * and the generic-payload plan renames every prop to 000..NNN and moves the real
 * names into C#. From that point the only place the schema exists in a readable
 * form is here.
 */

/** What a field is on the wire. The small set the two sides can express. */
/**
 * What a field is on the wire.
 *
 * `color32` is four channels in ONE word, which is what a class hand-packing
 * four byte fields was already sending. `qangle` is bit-for-bit a `vector` --
 * three unscaled floats -- and is a separate kind anyway, because three numbers
 * that are a facing and three that are a position are indistinguishable in a hex
 * dump and this is the column that tells them apart.
 */
export type NetFieldKind =
  'vector' | 'int' | 'uint' | 'string' | 'float' | 'bool' | 'color32' | 'qangle';

/** Which native shell carries a type's table. */
export type NetShell = 'netentity' | 'standalone';

export interface NetField {
  name: string;

  /** The C# spelling, so a field can be found in the source that declared it. */
  csType: string;

  kind: NetFieldKind;

  /** Where it sits in the blob. This is the number an offset bug gets wrong. */
  offset: number;

  /** What it occupies in the blob. Every offset after it depends on this. */
  bytes: number;

  /**
   * How wide the field is. Since packing this is its STORAGE width as well as
   * its wire width, and for a packed field it is narrower than `bytes` -- which
   * is the word the field shares rather than the field itself.
   */
  bits: number;

  /**
   * How far into `offset`'s dword the field's low bit sits.
   *
   * Optional because a reply from a build before packing does not carry it, and
   * a missing shift means zero rather than unknown.
   */
  bitShift?: number;

  /**
   * Whether the field shares its storage with another.
   *
   * Worth its own flag rather than being derived in the view: without it a
   * reader sees four fields at offset 0 across four bytes each and has no way
   * to tell that from an offset bug, which is the exact failure this panel
   * exists to make visible.
   */
  packed?: boolean;

  /**
   * The enum the field was declared as, where it was one.
   *
   * Not on the wire: an enum travels as the integer it is backed by and native
   * is told nothing about it. It is here because the value column would
   * otherwise read `3` where the author wrote `Snowfall`, which is the
   * readability this panel exists to give back.
   *
   * Optional, and absent rather than empty for a field that is not an enum.
   */
  enum?: NetEnum;
}

/** An enum-typed field's domain, as the schema reports it. */
export interface NetEnum {
  /** The C# type name, which is also `csType` for the field. */
  type: string;

  /** Whether the members are bits rather than alternatives. */
  flags: boolean;

  /** Every member, by value and then by name. */
  members: NetEnumMember[];
}

export interface NetEnumMember {
  name: string;
  value: number;
}

/**
 * Which tier a type falls in.
 *
 * `live` is false until Step 6 of the payload plan registers the tiers. Until
 * then `ceiling` is the tier this type WOULD land in, and a view must say so
 * rather than present a projection as a reading.
 */
export interface NetTierFit {
  /** Payload bytes of the tier, or -1 for a type too big for any of them. */
  ceiling: number;

  /** How much of the tier the layout uses, 0..1. */
  fill: number;

  /** Within a tenth of outgrowing the tier — the one change that needs a restart. */
  nearCeiling: boolean;

  live: boolean;
}

export interface NetType {
  className: string;

  /** The C# type that declared the fields. */
  typeName: string;

  shell: NetShell;

  /** The DT_ name the connect handshake pairs on. */
  table: string;

  typeId: number;
  typeIdHex: string;
  schemaHash: number;
  schemaHashHex: string;

  /** Bytes the generated layout occupies, aligned to four. */
  layoutBytes: number;

  /** Bytes native reserves for the class, aligned to sixteen. */
  blobBytes: number;

  /** Props in the built table: shared, the class's own, and the payload. */
  props: number;

  /**
   * How many of those props are generic payload words.
   *
   * ONE number for every class on the parallel hierarchy, because the blob it
   * covers is one length for every entity there -- the greediest declaration's,
   * whatever the class itself declared. Zero for a table with no payload.
   */
  payloadWords: number;

  fieldCount: number;

  /**
   * Whether this side actually put the class on the wire. A state may be
   * declared before its entity class exists, and "declared but not sent" must
   * not read the same as "sent and decoding to zero".
   */
  published: boolean;

  tier: NetTierFit;

  /** Only on /nettable/<id>; the schema listing leaves them out. */
  fields?: NetField[];
}

/** One projected tier, and the types that would land in it. */
export interface NetTier {
  name: string;
  payloadBytes: number;

  /** Payload words — one dword prop each. */
  words: number;

  /**
   * The props native BUILT, not a formula.
   *
   * It was `6 + payloadBytes / 4` here, from Part 3's table, and that is the
   * parallel hierarchy's number: the runtime-networked entity puts three real
   * props in front of its payload rather than six, because it networks no
   * origin, no angles and no model index and never has. A formula right about
   * one shell would have been quietly wrong about the other forever.
   */
  props: number;

  /**
   * Whether anything is actually on this tier.
   *
   * NOT whether the tier exists. All three are registered so the class list the
   * handshake negotiates is the full set and a type can grow into a bigger one
   * without a rebuilt client — so a registered, empty tier is the ordinary
   * state and means "room to grow into".
   */
  live: boolean;

  types: string[];
  count: number;
}

export interface NetSchema {
  types: NetType[];
  count: number;

  /** How many props every hierarchy entity sends before its own. */
  sharedProps: number;

  tiers: NetTier[];

  /**
   * Whether the tier rows above are a READING of what native registered.
   *
   * They are, since Step 6 of plan_nettable_generic.md — this used to be false
   * because nothing was registered and the rows were a projection of the
   * boundary rule. False now means something different and rarer: there was no
   * engine to ask, so there is nothing to show. Rendering an empty list as
   * "this build has no tiers" would be the projection's mistake arriving by the
   * opposite door.
   */
  tiersLive: boolean;

  /** Types too big for the largest tier, which the design could not network at all. */
  oversized: number;

  /**
   * Types this build could not put on the wire, and the loudest thing on this
   * pane when there are any.
   *
   * Two causes, both build-time facts about a fixed set of declarations: a TYPE
   * ID COLLISION — two classnames with the same FNV-1a, where the second would
   * be reachable by no lookup and the first would answer for both — and a
   * layout past the largest tier. Either one is a class whose entities pair
   * with the wrong managed half or with none, and neither has any other symptom.
   */
  refused: NetTypeRefusal[];

  error?: string;
}

/** One type this build could not publish. See NetSchema.refused. */
export interface NetTypeRefusal {
  className: string;
  typeIdHex: string;
  layoutBytes: number;
}

export interface NetTypeDetail extends NetType {
  /** Only on the error shape: what does exist, so a typo is obvious. */
  known?: string[];
  asked?: string;
  error?: string;
}

/** One field read out of a live blob. */
export interface NetDecodedField extends NetField {
  /** False when the blob was too short to hold it. No value is invented. */
  present: boolean;

  value: string;
  hex: string;

  /** Null for a vector and a string, which have no single number. */
  number: number | null;

  /** Touched by a byte that changed since the previous sample. */
  changed: boolean;
}

/** One row of the offset ruler. */
export interface NetPayloadRow {
  offset: number;
  hex: string;
  ascii: string;
}

/** A run of bytes that differ from the previous sample: [offset, length]. */
export type NetChangedRun = [number, number];

export interface NetPayloadChange {
  /** No previous sample. NOT the same as nothing having changed. */
  first: boolean;

  /** The blob is a different length than last time, so the diff is partial. */
  resized: boolean;

  sinceSeconds: number;
  bytes: number;
  runs: NetChangedRun[];
}

/**
 * What the server says about an entity's two paths while both carry it
 * (plan_nettable_generic.md Part 14 Step 3).
 *
 * FOUR states rather than a boolean, because only one of them is a failure.
 * "This entity has no payload" is true of nearly every entity in the map, and a
 * panel that painted those red would be ignored within a minute.
 */
export interface NetPayloadAgreement {
  state: 'agrees' | 'disagrees' | 'none' | 'unsupported';
  agrees: boolean;
  words: number;

  /**
   * The first word that disagreed, or -1. A WORD index and not a byte offset:
   * the blob bases differ between client and server by construction, so an
   * offset means nothing across the pair.
   */
  firstBadWord: number;

  /** False when the build cannot answer at all, which is not a disagreement. */
  live: boolean;
}

export interface NetPayload {
  index: number;
  handle: number;
  classname: string;

  /** Absent from a build older than the payload agreement check. */
  payload?: NetPayloadAgreement;

  /** Null for a class that networks nothing of its own and shares one table. */
  type: NetType | null;

  blob: {
    bytes: number;
    hex: string;
    rowBytes: number;
    rows: NetPayloadRow[];
  };

  fields: NetDecodedField[];
  changed: NetPayloadChange;

  layoutBytes?: number;

  /** The blob is shorter than the layout, which is always wrong. */
  blobShorterThanLayout?: boolean;

  error?: string;
}


/**
 * One field's share of the payload cost.
 *
 * `words` is what it costs on the wire: a changed payload dword is 39 bits, and
 * one changed BYTE costs the whole word it sits in. A word holding two changed
 * fields is charged to both, so this column can add up to more than the class
 * total — there is no non-arbitrary way to split one word between two fields,
 * and charging only the first would hide the second entirely.
 */
export interface NetTrafficField {
  name: string;
  words: number;
  bytesPerSecond: number;
}

/** One class's share of the payload cost. */
export interface NetTrafficClass {
  className: string;
  entityFramesSeen: number;
  entityFramesChanged: number;
  wordsTouched: number;
  bytesChanged: number;
  bytesPerSecond: number;
  recentBytesPerSecond: number;

  /**
   * How much of each changed word was real, 0..1. A class whose payload is
   * mostly padding shows up as a low number, which is exactly what Part 10's
   * packing is meant to improve.
   */
  wordFill: number;

  fields: NetTrafficField[];
}

/**
 * What the payload has cost, by class and by field.
 *
 * `available` and `sampling` are separate on purpose, and so is a quiet world:
 * a host that never wired the view, a view that is not sampling, and a game in
 * which nothing moved all produce an empty table and mean different things.
 */
export interface NetTraffic {
  available: boolean;
  sampling?: boolean;
  note?: string;
  elapsedSeconds?: number;
  trackedEntities?: number;
  bitsPerChangedWord?: number;
  windowSeconds?: number;
  unit?: string;
  fieldsNote?: string;
  classes: NetTrafficClass[];
}

//---------------------------------------------------------------------------
// Prediction.
//---------------------------------------------------------------------------

/**
 * One component of one field, as the client guessed it and as the server said.
 *
 * Empty for a string field and for padding — there is nothing to subtract — so
 * a reader of `components` must handle a field that has none rather than
 * assuming three.
 */
export interface PredictionComponent {
  predicted: number;
  server: number;
  delta: number;
}

/**
 * A field whose predicted bytes did not match the server's.
 *
 * `tolerance` is what the field DECLARED, and `toleranceRatio` is
 * `maxAbsDelta / tolerance` — **null** when nothing was declared. Null is not
 * zero and it is not "fine": it means no tolerance was engaged, so any
 * difference at all is a break.
 *
 * `tolerable` is about the KIND: only a float, a vector or a qangle can ever be
 * given a tolerance. A field with `tolerable: false` and no ratio is a
 * different thing from a float that simply declared none, and conflating the
 * two would have a reader chasing a tolerance that could never have existed.
 */
export interface PredictionField {
  name: string;
  kind: number;
  kindName: string;
  offset: number;
  bytes: number;
  tolerance: number;
  tolerable: boolean;

  /** The difference was inside the declared tolerance and forgiven. */
  tolerated: boolean;
  differingBytes: number;
  maxAbsDelta: number;
  toleranceRatio: number | null;
  components: PredictionComponent[];
}

/**
 * One compare that found a difference.
 *
 * `mispredicted` is false when every differing field was inside its tolerance —
 * those records are kept on purpose, because a tolerated difference that is
 * GROWING is the only early warning there is.
 */
export interface PredictionRecord {
  sequence: number;
  command: number;
  typeId: number;
  class: string;
  blobBytes: number;
  standingBytes: number;
  firstDiffOffset: number;
  toleratedFields: number;
  mispredicted: boolean;
  atUtc: string;
  fields: PredictionField[];
}

/**
 * What the client's prediction has been getting wrong.
 *
 * `observed` counts compares that found ANY difference. A prediction that held
 * byte for byte is never recorded, so this is NOT the packet count and
 * `mispredicted / observed` is NOT the game's misprediction rate — it is the
 * share of DIFFERING compares that broke through their tolerances. Presenting
 * it as the former would report a game predicting almost perfectly as one
 * failing nearly always.
 *
 * `available` is false in a process with no client: prediction is client-side,
 * so a dedicated server has nothing to report and that is not an error.
 */
export interface Prediction {
  available: boolean;
  note?: string;
  capacity?: number;
  held?: number;
  nextSequence?: number;
  observed?: number;
  mispredicted?: number;
  toleratedOnly?: number;
  unit?: string;
  records: PredictionRecord[];
}
