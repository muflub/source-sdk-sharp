/*
 * The net tables tab.
 *
 * What matters here is what the panes REFUSE to say. A tier projection must not
 * read as a tier reading, a type nobody published must not read as one whose
 * fields decode to zero, a first sample must not read as "nothing changed", and
 * a field past the end of a blob must not be given a value. Each of those is a
 * case where the comfortable rendering is the misleading one, and this tab is
 * the thing everything downstream of Step 3 believes.
 */

import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { NetTablesTab } from '../NetTablesTab';
import type { NetPayload, NetSchema, NetType, NetTypeDetail } from '../../types';

vi.mock('../../api', () => ({
  getNetTables: vi.fn(), getNetTable: vi.fn(), getPayload: vi.fn(),
}));

const api = await import('../../api') as unknown as {
  getNetTables: ReturnType<typeof vi.fn>;
  getNetTable: ReturnType<typeof vi.fn>;
  getPayload: ReturnType<typeof vi.fn>;
};

const barrel: NetType = {
  className: 'ss_barrel',
  typeName: 'BarrelNetState',
  shell: 'standalone',
  table: 'DT_ss_barrel',
  typeId: 0x11223344,
  typeIdHex: '0x11223344',
  schemaHash: 0x55667788,
  schemaHashHex: '0x55667788',
  layoutBytes: 12,
  blobBytes: 16,
  props: 10,
  payloadWords: 4,
  fieldCount: 2,
  published: true,
  tier: { ceiling: 32, fill: 0.375, nearCeiling: false, live: false },
};

// A type close enough to its tier ceiling to be flagged, and one that was never
// put on the wire. Both are states the panel has to render differently.
const crowded: NetType = {
  ...barrel,
  className: 'ss_crowded',
  typeName: 'CrowdedNetState',
  table: 'DT_ss_crowded',
  layoutBytes: 30,
  published: false,
  tier: { ceiling: 32, fill: 0.9375, nearCeiling: true, live: false },
};

// The tier rows are a READING of what native registered since Step 6, and the
// props are what it BUILT rather than a formula — which is why they are written
// out here as data. The 32-byte tier is live because entities are on it; the
// 256 one is registered and empty, which is the ordinary state and means "room
// to grow into" rather than "not implemented".
const schema: NetSchema = {
  types: [barrel, crowded],
  count: 2,
  sharedProps: 4,
  tiersLive: true,
  oversized: 0,
  refused: [],
  tiers: [
    { name: 'DT_SourceSharpStdEnt32', payloadBytes: 32, words: 8, props: 14, live: true,
      types: ['ss_barrel', 'ss_crowded'], count: 2 },
    { name: 'DT_SourceSharpStdEnt256', payloadBytes: 256, words: 64, props: 70, live: false,
      types: [], count: 0 },
  ],
};

const barrelDetail: NetTypeDetail = {
  ...barrel,
  fields: [
    { name: 'Health', csType: 'int', kind: 'int', offset: 0, bytes: 4, bits: 32 },
    { name: 'Fuse', csType: 'float', kind: 'float', offset: 4, bytes: 4, bits: 32 },
  ],
};

const payload: NetPayload = {
  index: 42,
  handle: 43981,
  classname: 'ss_barrel',
  type: barrel,
  blob: {
    bytes: 16,
    hex: '0700000000004040000000000000000',
    rowBytes: 16,
    rows: [{ offset: 0, hex: '07000000000040400000000000000000', ascii: '......@@........' }],
  },
  fields: [
    { name: 'Health', csType: 'int', kind: 'int', offset: 0, bytes: 4, bits: 32,
      present: true, value: '7', hex: '07000000', number: 7, changed: false },
    { name: 'Fuse', csType: 'float', kind: 'float', offset: 4, bytes: 4, bits: 32,
      present: true, value: '3', hex: '00004040', number: 3, changed: true },
  ],
  changed: { first: false, resized: false, sinceSeconds: 1, bytes: 2, runs: [[4, 2]] },
  layoutBytes: 12,
  blobShorterThanLayout: false,
};

beforeEach(() => {
  vi.clearAllMocks();
  api.getNetTables.mockResolvedValue(schema);
  api.getNetTable.mockResolvedValue(barrelDetail);
  api.getPayload.mockResolvedValue(payload);
});

const showSchema = async (): Promise<void> => {
  render(<NetTablesTab visible />);
  await screen.findByText('ss_barrel');
};

const showInspector = async (): Promise<void> => {
  render(<NetTablesTab visible />);
  await screen.findByText('ss_barrel');
  await userEvent.click(screen.getByRole('button', { name: 'Entity inspector' }));
};

describe('the schema pane', () => {
  it('lists each type with its table and its identity', async () => {
    await showSchema();

    const row = screen.getByText('ss_barrel').closest('tr') as HTMLElement;

    expect(within(row).getByText('DT_ss_barrel')).toBeTruthy();
    expect(within(row).getByText('0x11223344')).toBeTruthy();
    expect(within(row).getByText('0x55667788')).toBeTruthy();
  });

  // The one thing the panel must not do: present a projection as a reading. The
  // tiers are not registered until Step 6, and a page that showed them as live
  // would be the first thing to mislead somebody.
  // The tiers are a READING since Step 6. What the pane has to keep saying is
  // WHICH — a projection shown as a reading misleads, and so does an absent
  // reading shown as "this build has no tiers".
  it('says the tiers are read from the game', async () => {
    await showSchema();

    expect(screen.getByText(/tiers live/)).toBeTruthy();
    expect(screen.getByText(/read from the game/)).toBeTruthy();
  });

  it('reports the prop count native built, not a formula', async () => {
    await showSchema();
    expect(screen.getByText(/32 byte payload · 14 props/)).toBeTruthy();
  });

  // A registered tier with nothing on it is the ordinary state — the class list
  // carries all three so a type can grow into a bigger one without a rebuilt
  // client — so it must read as room rather than as a gap.
  it('marks a registered tier with nothing on it as empty rather than missing', async () => {
    await showSchema();
    expect(screen.getByText(/256 byte payload · 70 props · empty/)).toBeTruthy();
  });

  // The loudest thing on the pane. A type id collision or a layout past the
  // largest tier is a class whose entities pair with the wrong managed half or
  // with none, and neither has any other symptom.
  it('banners a type the build could not publish', async () => {
    api.getNetTables.mockResolvedValue({
      ...schema,
      refused: [{ className: 'ss_collides', typeIdHex: '0x5E4DAA9D', layoutBytes: 40 }],
    });

    await showSchema();

    expect(screen.getByText(/could not be\s+published/)).toBeTruthy();
    expect(screen.getByText('ss_collides')).toBeTruthy();
  });

  // Growing past a tier is the one schema change that still needs a restart, so
  // it should be seen coming rather than discovered at connect.
  it('flags a type within a tenth of its tier ceiling', async () => {
    await showSchema();

    const banner = screen.getByText(/within a tenth/).closest('.banner') as HTMLElement;
    expect(within(banner).getByText('ss_crowded')).toBeTruthy();
    expect(within(banner).getByText(/30 of 32 bytes/)).toBeTruthy();
  });

  it('does not flag a type well inside its tier', async () => {
    api.getNetTables.mockResolvedValue({ ...schema, types: [barrel] });
    await showSchema();

    expect(screen.queryByText(/within a tenth/)).toBeNull();
  });

  // "Declared but not sent" and "sent and decoding to zero" are the same picture
  // from outside, and only one of them is a bug.
  it('marks a type that was never put on the wire', async () => {
    await showSchema();

    // The name also appears in the tier rollup and in the ceiling warning, so
    // the row is found by its cell rather than by the first match.
    const cell = screen.getAllByText('ss_crowded').find(node => node.tagName === 'TD')!;
    const row = cell.closest('tr') as HTMLElement;

    expect(within(row).getByText('not on the wire')).toBeTruthy();
  });

  // The fields are the bulk of the schema, so the listing leaves them out and a
  // row asks for its own when it is opened.
  it('reads a type\'s fields only when its row is opened', async () => {
    await showSchema();
    expect(api.getNetTable).not.toHaveBeenCalled();

    const row = screen.getByText('ss_barrel').closest('tr') as HTMLElement;
    await userEvent.click(within(row).getByRole('button', { name: '▸' }));

    expect(api.getNetTable).toHaveBeenCalledWith('ss_barrel');
    expect(await screen.findByText('Health')).toBeTruthy();
  });

  it('shows storage and wire width as separate numbers', async () => {
    await showSchema();

    const row = screen.getByText('ss_barrel').closest('tr') as HTMLElement;
    await userEvent.click(within(row).getByRole('button', { name: '▸' }));

    const health = (await screen.findByText('Health')).closest('tr') as HTMLElement;

    // Units on both, because the two numbers used to be bare and adjacent and a
    // reader had to remember which column was which. `4 B` of storage carrying
    // `32 b` of wire cannot be read the other way round.
    expect(within(health).getByText('4 B')).toBeTruthy();
    expect(within(health).getByText('32 b')).toBeTruthy();
  });

  // PACKING made two fields able to report the same offset, and a panel that
  // showed only the offset would render that as an offset bug -- which is the
  // one thing this panel exists to make visible, so it must not produce a false
  // one. The placement column is what tells them apart.
  it('says where inside its word a packed field sits', async () => {
    api.getNetTable.mockResolvedValue({
      ...barrelDetail,
      fields: [
        { name: 'Wide', csType: 'byte', kind: 'uint', offset: 0, bytes: 4, bits: 8,
          bitShift: 0, packed: true },
        { name: 'Broken', csType: 'bool', kind: 'bool', offset: 0, bytes: 4, bits: 1,
          bitShift: 16, packed: true },
        { name: 'Corner', csType: 'Vector', kind: 'vector', offset: 4, bytes: 12, bits: 32 },
      ],
    });

    await showSchema();

    const row = screen.getByText('ss_barrel').closest('tr') as HTMLElement;
    await userEvent.click(within(row).getByRole('button', { name: '▸' }));

    const wide = (await screen.findByText('Wide')).closest('tr') as HTMLElement;
    const broken = screen.getByText('Broken').closest('tr') as HTMLElement;
    const corner = screen.getByText('Corner').closest('tr') as HTMLElement;

    // Same offset, different bits -- which is the whole of what packing did.
    expect(within(wide).getByText('bit 0..7')).toBeTruthy();
    expect(within(broken).getByText('bit 16..16')).toBeTruthy();

    // And a field that takes its storage whole says so rather than being blank,
    // because a blank cell reads as "not known" and this is known.
    expect(within(corner).getByText('whole')).toBeTruthy();
  });

  // THE READING THAT STARTED THIS. spotlight_end's four colour channels were
  // rendered as four rows each reporting BYTES 4, inside a class the same pane
  // called 8 bytes long -- so the column that looked like a per-field cost added
  // up to sixteen bytes of colour, and the packer got the blame for the view's
  // arithmetic.
  //
  // The fix is that a shared word is a GROUP: its size is stated once, on the
  // word, and no field in it prints a byte count at all, because a packed field
  // has no byte cost of its own. There is nothing left to add up wrongly.
  it('states a shared word\'s size once and never per field', async () => {
    api.getNetTable.mockResolvedValue({
      ...barrelDetail,
      fields: [
        { name: 'Red', csType: 'byte', kind: 'uint', offset: 0, bytes: 4, bits: 8,
          bitShift: 0, packed: true },
        { name: 'Green', csType: 'byte', kind: 'uint', offset: 0, bytes: 4, bits: 8,
          bitShift: 8, packed: true },
        { name: 'Blue', csType: 'byte', kind: 'uint', offset: 0, bytes: 4, bits: 8,
          bitShift: 16, packed: true },
        { name: 'Alpha', csType: 'byte', kind: 'uint', offset: 0, bytes: 4, bits: 8,
          bitShift: 24, packed: true },
        { name: 'LightScale', csType: 'float', kind: 'float', offset: 4, bytes: 4, bits: 32 },
      ],
    });

    await showSchema();

    const row = screen.getByText('ss_barrel').closest('tr') as HTMLElement;
    await userEvent.click(within(row).getByRole('button', { name: '▸' }));

    await screen.findByText('Red');

    // ONE word header for the four, saying the size and how full it is.
    const words = document.querySelectorAll('tr.packed-word');
    expect(words.length).toBe(1);
    expect(words[0]!.textContent).toContain('4 bytes holding 4 fields');
    expect(words[0]!.textContent).toContain('32 of 32 bits used');

    // And not one of the four claims a byte count. This is the assertion that
    // would have caught the original defect: `4 B` appearing four times over is
    // exactly what a reader summed to sixteen.
    for (const name of ['Red', 'Green', 'Blue', 'Alpha']) {
      const field = screen.getByText(name).closest('tr') as HTMLElement;

      expect(within(field).queryByText('4 B')).toBeNull();
      expect(within(field).getByText('shared')).toBeTruthy();
      expect(within(field).getByText('8 b')).toBeTruthy();

      // The declared type, not the word's. Four `byte`s in a `uint` word.
      expect(within(field).getByText('byte')).toBeTruthy();
    }

    // The float after them is not in the group and does state its bytes, which
    // is the control: `4 B` still appears exactly once on the whole table.
    const scale = screen.getByText('LightScale').closest('tr') as HTMLElement;
    expect(within(scale).getByText('4 B')).toBeTruthy();
    expect(screen.getAllByText('4 B').length).toBe(1);
  });

  // A word with ONE narrow field in it is still a word, and still not that
  // field's cost. Grouping only when two fields share would have left the lone
  // bool reporting four bytes -- the same lie, one row at a time.
  it('groups a word holding a single narrow field too, and says how much is spare', async () => {
    api.getNetTable.mockResolvedValue({
      ...barrelDetail,
      fields: [
        { name: 'Broken', csType: 'bool', kind: 'bool', offset: 0, bytes: 4, bits: 1,
          bitShift: 0, packed: true },
      ],
    });

    await showSchema();

    const row = screen.getByText('ss_barrel').closest('tr') as HTMLElement;
    await userEvent.click(within(row).getByRole('button', { name: '▸' }));

    await screen.findByText('Broken');

    const words = document.querySelectorAll('tr.packed-word');
    expect(words.length).toBe(1);
    expect(words[0]!.textContent).toContain('4 bytes holding 1 field');
    expect(words[0]!.textContent).toContain('1 of 32 bits used');

    const broken = screen.getByText('Broken').closest('tr') as HTMLElement;
    expect(within(broken).queryByText('4 B')).toBeNull();
    expect(within(broken).getByText('1 b')).toBeTruthy();
  });

  // Two words at the SAME offset would be a packer bug, and the grouping must
  // show it as two rather than quietly merge it into one. Grouping by offset
  // alone would have hidden precisely the failure this pane exists to catch.
  it('keeps non-adjacent fields at one offset as separate words', async () => {
    api.getNetTable.mockResolvedValue({
      ...barrelDetail,
      fields: [
        { name: 'First', csType: 'byte', kind: 'uint', offset: 0, bytes: 4, bits: 8,
          bitShift: 0, packed: true },
        { name: 'Between', csType: 'float', kind: 'float', offset: 4, bytes: 4, bits: 32 },
        { name: 'Second', csType: 'byte', kind: 'uint', offset: 0, bytes: 4, bits: 8,
          bitShift: 8, packed: true },
      ],
    });

    await showSchema();

    const row = screen.getByText('ss_barrel').closest('tr') as HTMLElement;
    await userEvent.click(within(row).getByRole('button', { name: '▸' }));

    await screen.findByText('First');

    const words = document.querySelectorAll('tr.packed-word');
    expect(words.length).toBe(2);
    expect(words[0]!.textContent).toContain('holding 1 field');
    expect(words[1]!.textContent).toContain('holding 1 field');
  });

  // ENUMS (plan_nettable_generic.md Step 11). The kind column says `uint` for
  // every enum, honestly, because that is what goes on the wire -- so the member
  // names are the only thing that turns a decoded 3 back into what the author
  // wrote, and this panel is where the payload's readability was supposed to go.
  it('lists an enum field\'s members under it', async () => {
    api.getNetTable.mockResolvedValue({
      ...barrelDetail,
      fields: [
        { name: 'Health', csType: 'int', kind: 'int', offset: 0, bytes: 4, bits: 32 },
        {
          name: 'Lighting', csType: 'DynamicLightFlags', kind: 'uint',
          offset: 4, bytes: 4, bits: 4, bitShift: 0, packed: true,
          enum: {
            type: 'DynamicLightFlags',
            flags: true,
            members: [
              { name: 'None', value: 0 },
              { name: 'NoWorldIllumination', value: 1 },
              { name: 'NoModelIllumination', value: 2 },
            ],
          },
        },
      ],
    });

    await showSchema();

    const row = screen.getByText('ss_barrel').closest('tr') as HTMLElement;
    await userEvent.click(within(row).getByRole('button', { name: '▸' }));

    // The type is named where a reader looks for it, and the members are one
    // row below rather than in a tooltip: a value column reading "3" and a name
    // nobody can reach is the loss this step exists to undo.
    expect(await screen.findByText('DynamicLightFlags')).toBeTruthy();
    expect(screen.getByText('flags')).toBeTruthy();
    expect(screen.getByText('NoWorldIllumination = 1')).toBeTruthy();
    expect(screen.getByText('NoModelIllumination = 2')).toBeTruthy();

    // And a field that is not an enum grows no row. A panel that showed an
    // empty members strip under every int would bury the ones that mean
    // something.
    const health = screen.getByText('Health').closest('tr') as HTMLElement;
    expect(health.nextElementSibling?.className).not.toContain('enum-members');
  });

  it('filters the type list', async () => {
    await showSchema();

    await userEvent.type(screen.getByPlaceholderText('filter types'), 'crowded');

    // The TABLE is filtered. The tier rollup above it is a rollup and stays
    // whole -- a total that changed with the filter would not be a total.
    const named = (name: string): HTMLElement[] =>
      screen.queryAllByText(name).filter(node => node.tagName === 'TD');

    expect(named('ss_barrel')).toHaveLength(0);
    expect(named('ss_crowded')).toHaveLength(1);
  });
});

describe('the entity inspector', () => {
  it('draws the blob with each field\'s own bytes attributed', async () => {
    await showInspector();
    await userEvent.type(screen.getByLabelText(/entity index/), '42');
    await userEvent.click(screen.getByRole('button', { name: 'Sample' }));

    await screen.findByText('0x0000');

    // The title is what says which field a byte belongs to, and it is the claim
    // the whole grid rests on.
    const first = screen.getByTitle(/0x0000/);
    expect(first.getAttribute('title')).toContain('Health');

    expect(screen.getByTitle(/0x0004/).getAttribute('title')).toContain('Fuse');
  });

  it('tints the bytes that moved and only those', async () => {
    await showInspector();
    await userEvent.type(screen.getByLabelText(/entity index/), '42');
    await userEvent.click(screen.getByRole('button', { name: 'Sample' }));

    await screen.findByText('0x0000');

    const tinted = Array.from(document.querySelectorAll('.hex-byte[data-changed="true"]'));
    expect(tinted).toHaveLength(2);
  });

  it('names the fields that moved', async () => {
    await showInspector();
    await userEvent.type(screen.getByLabelText(/entity index/), '42');
    await userEvent.click(screen.getByRole('button', { name: 'Sample' }));

    expect(await screen.findByText(/2 byte\(s\) changed.*Fuse/)).toBeTruthy();
  });

  // A first sample is a different fact from "nothing changed", and rendering
  // them alike would make a still entity and a newly watched one identical.
  it('says a first sample is a first sample', async () => {
    api.getPayload.mockResolvedValue({
      ...payload,
      fields: payload.fields.map(f => ({ ...f, changed: false })),
      changed: { first: true, resized: false, sinceSeconds: 0, bytes: 0, runs: [] },
    });

    await showInspector();
    await userEvent.type(screen.getByLabelText(/entity index/), '42');
    await userEvent.click(screen.getByRole('button', { name: 'Sample' }));

    expect(await screen.findByText(/First sample/)).toBeTruthy();
    expect(screen.queryByText(/Nothing changed/)).toBeNull();
  });

  it('says so plainly when nothing moved', async () => {
    api.getPayload.mockResolvedValue({
      ...payload,
      fields: payload.fields.map(f => ({ ...f, changed: false })),
      changed: { first: false, resized: false, sinceSeconds: 1, bytes: 0, runs: [] },
    });

    await showInspector();
    await userEvent.type(screen.getByLabelText(/entity index/), '42');
    await userEvent.click(screen.getByRole('button', { name: 'Sample' }));

    expect(await screen.findByText(/Nothing changed/)).toBeTruthy();
  });

  // Bytes moving that no field claims means something is writing into padding,
  // and an empty list of names would hide it.
  it('says when the bytes that moved belong to no field', async () => {
    api.getPayload.mockResolvedValue({
      ...payload,
      fields: payload.fields.map(f => ({ ...f, changed: false })),
      changed: { first: false, resized: false, sinceSeconds: 1, bytes: 1, runs: [[13, 1]] },
    });

    await showInspector();
    await userEvent.type(screen.getByLabelText(/entity index/), '42');
    await userEvent.click(screen.getByRole('button', { name: 'Sample' }));

    expect(await screen.findByText(/in bytes no field claims/)).toBeTruthy();
  });

  // No value is invented for a field past the end of the blob: a fabricated
  // zero is a lie the reader would act on.
  it('refuses to show a value for a field past the end of the blob', async () => {
    api.getPayload.mockResolvedValue({
      ...payload,
      fields: [payload.fields[0]!, { ...payload.fields[1]!, present: false, value: '', number: null }],
    });

    await showInspector();
    await userEvent.type(screen.getByLabelText(/entity index/), '42');
    await userEvent.click(screen.getByRole('button', { name: 'Sample' }));

    expect(await screen.findByText('past the end of the blob')).toBeTruthy();
  });

  it('warns loudly when the blob is shorter than the layout', async () => {
    api.getPayload.mockResolvedValue({ ...payload, blobShorterThanLayout: true, blob: { ...payload.blob, bytes: 8 } });

    await showInspector();
    await userEvent.type(screen.getByLabelText(/entity index/), '42');
    await userEvent.click(screen.getByRole('button', { name: 'Sample' }));

    expect(await screen.findByText(/point past the entity/)).toBeTruthy();
  });

  // Most of the hierarchy networks nothing of its own and shares one table.
  // Decoding those bytes against somebody else's layout would be far worse than
  // saying there is nothing to decode them with.
  it('says there is no layout rather than decoding against the wrong one', async () => {
    api.getPayload.mockResolvedValue({ ...payload, type: null, fields: [] });

    await showInspector();
    await userEvent.type(screen.getByLabelText(/entity index/), '42');
    await userEvent.click(screen.getByRole('button', { name: 'Sample' }));

    expect(await screen.findByText(/networks no state of its own/)).toBeTruthy();
    expect(screen.getByText(/nothing to decode these bytes with/)).toBeTruthy();
  });

  // While both paths carry an entity, the panel has to say whether they agree —
  // and has to keep quiet about the three answers that are not failures, or the
  // one that is will be invisible among them.
  it('says the two paths agree while both carry the entity', async () => {
    api.getPayload.mockResolvedValue({
      ...payload,
      payload: { state: 'agrees', agrees: true, words: 8, firstBadWord: -1, live: true },
    });

    await showInspector();
    await userEvent.type(screen.getByLabelText(/entity index/), '42');
    await userEvent.click(screen.getByRole('button', { name: 'Sample' }));

    expect(await screen.findByText(/agree on all 8 word/)).toBeTruthy();
  });

  it('names the first word that disagrees, not a byte offset', async () => {
    api.getPayload.mockResolvedValue({
      ...payload,
      payload: { state: 'disagrees', agrees: false, words: 8, firstBadWord: 3, live: true },
    });

    await showInspector();
    await userEvent.type(screen.getByLabelText(/entity index/), '42');
    await userEvent.click(screen.getByRole('button', { name: 'Sample' }));

    expect(await screen.findByText(/disagrees with the typed props from word/)).toBeTruthy();
    expect(screen.getByText(/3 of 8/)).toBeTruthy();
  });

  it('stays quiet about an entity that simply has no payload', async () => {
    api.getPayload.mockResolvedValue({
      ...payload,
      payload: { state: 'none', agrees: false, words: 0, firstBadWord: -1, live: true },
    });

    await showInspector();
    await userEvent.type(screen.getByLabelText(/entity index/), '42');
    await userEvent.click(screen.getByRole('button', { name: 'Sample' }));

    expect(await screen.findByText(/The blob as it is/)).toBeTruthy();
    expect(screen.queryByText(/disagrees with the typed props/)).toBeNull();
    expect(screen.queryByText(/agree on all/)).toBeNull();
  });

  it('reports an entity that is not there rather than an empty payload', async () => {
    api.getPayload.mockResolvedValue({ error: 'no entity at that index', index: 999 });

    await showInspector();
    await userEvent.type(screen.getByLabelText(/entity index/), '999');
    await userEvent.click(screen.getByRole('button', { name: 'Sample' }));

    expect(await screen.findByText('no entity at that index')).toBeTruthy();
  });
});

// The payload is the one thing in this tab that costs the game a frame, so it
// must not be sampled by a page sitting on another tab.
it('does not sample a payload while the tab is hidden', async () => {
  render(<NetTablesTab visible={false} />);

  await new Promise(resolve => setTimeout(resolve, 10));
  expect(api.getPayload).not.toHaveBeenCalled();
});
