/*
 * The convar editor.
 *
 * The behaviour worth pinning down is the read-back: every write is followed by
 * a read from the engine, and the row shows what came back rather than what was
 * sent. A convar can clamp what it was given, and showing the requested value
 * would hide exactly the case worth seeing.
 */

import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ConVarsTab } from '../ConVars';
import type { ConVar } from '../../types';

vi.mock('../../api', () => ({
  getConVars: vi.fn(),
  writeConVar: vi.fn(),
}));

const api = await import('../../api') as unknown as {
  getConVars: ReturnType<typeof vi.fn>;
  writeConVar: ReturnType<typeof vi.fn>;
};

const convar = (over: Partial<ConVar>): ConVar => ({
  name: 'cs_sv_spawn_health',
  value: '100',
  default: '100',
  help: 'Health a player spawns with',
  type: 'int',
  flags: 'None',
  registered: true,
  min: null,
  max: null,
  source: 'managed',
  declaredBy: 'SpawnHealthConVar',
  ...over,
});

beforeEach(() => {
  vi.clearAllMocks();
  api.getConVars.mockResolvedValue({ convars: [], count: 0 });
});

async function show(convars: ConVar[]): Promise<void> {
  api.getConVars.mockResolvedValue({ convars, count: convars.length });
  render(<ConVarsTab visible />);
  await screen.findByText(convars[0]!.name);
}

describe('the editor', () => {
  it('is a number box for an int', async () => {
    await show([convar({ type: 'int' })]);
    expect((screen.getByDisplayValue('100') as HTMLInputElement).type).toBe('number');
  });

  // Scoped to the table: the toolbar has a checkbox of its own.
  it('is a checkbox for a bool', async () => {
    await show([convar({ name: 'cs_sv_friendly_fire', type: 'bool', value: '1', default: '1' })]);

    const box = within(screen.getByRole('table')).getByRole('checkbox') as HTMLInputElement;
    expect(box.checked).toBe(true);
  });

  it('is a text box for a string', async () => {
    await show([convar({ name: 'cs_sv_greeting', type: 'string', value: 'hello', default: 'hello' })]);
    expect((screen.getByDisplayValue('hello') as HTMLInputElement).type).toBe('text');
  });
});

describe('writing', () => {
  // The whole point: what the engine says it holds, not what it was asked for.
  it('shows the value the engine read back, not the one that was sent', async () => {
    await show([convar({ value: '100', default: '100' })]);

    api.writeConVar.mockResolvedValue({ name: 'cs_sv_spawn_health', value: '250' });

    const box = screen.getByDisplayValue('100');
    await userEvent.clear(box);
    await userEvent.type(box, '9999');
    await userEvent.tab();

    await waitFor(() => expect(screen.getByDisplayValue('250')).toBeTruthy());
    expect(api.writeConVar).toHaveBeenCalledWith('cs_sv_spawn_health', '9999');
  });

  // Under the row's menu rather than beside every value: resetting is an
  // occasional thing, and a button per row for it was a column of Defaults
  // running down the middle of the values you were trying to read.
  it('sends the declared default from the row menu', async () => {
    await show([convar({ value: '250', default: '100' })]);
    api.writeConVar.mockResolvedValue({ name: 'cs_sv_spawn_health', value: '100' });

    await userEvent.click(screen.getByRole('menuitem', { name: 'Reset to default' }));

    expect(api.writeConVar).toHaveBeenCalledWith('cs_sv_spawn_health', '100');
  });

  // The value is what is being read; that it is not the default belongs next to
  // it rather than a column away.
  it('marks a value that is not the default', async () => {
    await show([convar({ value: '250', default: '100' })]);

    expect(screen.getByTitle(/changed from its default of 100/)).toBeTruthy();
  });

  it('marks nothing when the value is the default', async () => {
    await show([convar({ value: '100', default: '100' })]);

    expect(screen.queryByTitle(/changed from its default/)?.hidden).toBe(true);
  });
});

// A refused convar still lists, because the setting is real and something is
// reading it — but writing to it goes nowhere, so offering an edit would be
// worse than offering none.
describe('a convar the engine refused', () => {
  it('is listed, and cannot be edited', async () => {
    await show([convar({ registered: false })]);

    expect((screen.getByDisplayValue('100') as HTMLInputElement).disabled).toBe(true);

    expect((screen.getByRole('menuitem', { name: 'Reset to default' }) as HTMLButtonElement).disabled)
      .toBe(true);
    expect(screen.getByText(/writes go nowhere/)).toBeTruthy();
  });
});

describe('filtering', () => {
  it('matches the name and the help text', async () => {
    await show([
      convar({ name: 'cs_sv_spawn_health' }),
      convar({ name: 'cs_sv_greeting', help: 'Greeting logged when a player joins' }),
    ]);

    await userEvent.type(screen.getByPlaceholderText('filter convars'), 'joins');

    expect(screen.queryByText('cs_sv_spawn_health')).toBeNull();
    expect(screen.getByText('cs_sv_greeting')).toBeTruthy();
  });

  it('can show only what differs from its default', async () => {
    await show([
      convar({ name: 'cs_sv_spawn_health', value: '100', default: '100' }),
      convar({ name: 'cs_sv_damage_scale', value: '2', default: '1' }),
    ]);

    await userEvent.click(screen.getByLabelText(/changed from default/));

    expect(screen.queryByText('cs_sv_spawn_health')).toBeNull();
    expect(screen.getByText('cs_sv_damage_scale')).toBeTruthy();
  });
});

/*
 * The engine's own convars, which are a different kind of row: no type was ever
 * declared for one, so the tool must not pretend otherwise.
 */
describe('a native convar', () => {
  const native = (over: Partial<ConVar> = {}): ConVar => convar({
    name: 'sv_gravity',
    value: '800',
    default: '600',
    help: 'World gravity',
    type: 'unknown',
    flags: 'GameDll, Replicated',
    source: 'native',
    declaredBy: 'server.dll',
    ...over,
  });

  it('is a text box, never a checkbox, whatever its value looks like', async () => {
    await show([native({ value: '1', default: '0' })]);

    const box = screen.getByDisplayValue('1') as HTMLInputElement;
    expect(box.type).toBe('text');
    expect(within(screen.getByRole('table')).queryByRole('checkbox')).toBeNull();
  });

  // In a column of its own rather than appended to the name: it reads down the
  // column, and it sorts.
  it('says whose it is, in the Declared by column', async () => {
    await show([native()]);

    const row = within(screen.getByText('sv_gravity').closest('tr') as HTMLElement);
    expect(row.getByText('server.dll')).toBeTruthy();

    // The name cell is the name and nothing else.
    expect(screen.getByRole('cell', { name: 'sv_gravity' })).toBeTruthy();
  });

  it('gives a managed convar the same column, holding its C# class', async () => {
    await show([convar({ declaredBy: 'SpawnHealthConVar' })]);

    const row = within(screen.getByText('cs_sv_spawn_health').closest('tr') as HTMLElement);
    expect(row.getByText('SpawnHealthConVar')).toBeTruthy();
  });

  it('is still editable: the console can set it, so this can', async () => {
    await show([native()]);
    expect((screen.getByDisplayValue('800') as HTMLInputElement).disabled).toBe(false);
  });

  // The default is this mod's registry, because the engine's costs the game a
  // frame to walk and this page polls.
  it('is only asked for when the source picker says so', async () => {
    await show([convar({})]);
    expect(api.getConVars).toHaveBeenCalledWith('managed');

    await userEvent.selectOptions(screen.getByLabelText('source'), 'all');

    await waitFor(() => expect(api.getConVars).toHaveBeenCalledWith('all'));
  });
});

/*
 * Filtering by flag. Among this mod's sixty convars it is a convenience; among
 * the engine's four thousand it is the question actually worth asking.
 */
describe('filtering by flag', () => {
  const listed = async (): Promise<void> => show([
    convar({ name: 'cs_sv_greeting', flags: 'Archive, Replicated' }),
    convar({ name: 'cs_sv_cheatish', flags: 'Cheat' }),
    convar({ name: 'cs_sv_plain', flags: 'None' }),
  ]);

  it('offers the flags that are present, and no others', async () => {
    await listed();

    const options = [...(screen.getByLabelText('flag') as HTMLSelectElement).options].map(o => o.value);

    expect(options).toContain('Archive');
    expect(options).toContain('Cheat');
    expect(options).not.toContain('UserInfo');
  });

  it('keeps only the rows carrying it', async () => {
    await listed();

    await userEvent.selectOptions(screen.getByLabelText('flag'), 'Archive');

    expect(screen.getByText('cs_sv_greeting')).toBeTruthy();
    expect(screen.queryByText('cs_sv_cheatish')).toBeNull();
    expect(screen.queryByText('cs_sv_plain')).toBeNull();
  });

  // "Which of these carry no flags at all" is a real question, and picking one
  // flag cannot answer it.
  it('can ask for the ones with no flags at all', async () => {
    await listed();

    await userEvent.selectOptions(screen.getByLabelText('flag'), 'none');

    expect(screen.getByText('cs_sv_plain')).toBeTruthy();
    expect(screen.queryByText('cs_sv_greeting')).toBeNull();
  });
});

/*
 * Bounds. The engine clamps to them on the way in, so the box says what the
 * value is allowed to be rather than letting somebody send one that will not
 * stick.
 */
describe('a convar with bounds', () => {
  it('puts them on the number box', async () => {
    await show([convar({ type: 'float', value: '1', default: '1', min: 0, max: 10 })]);

    const box = screen.getByDisplayValue('1') as HTMLInputElement;
    expect(box.min).toBe('0');
    expect(box.max).toBe('10');
  });

  it('leaves the box unbounded when the convar is', async () => {
    await show([convar({ type: 'float', value: '1', default: '1' })]);

    const box = screen.getByDisplayValue('1') as HTMLInputElement;
    expect(box.min).toBe('');
    expect(box.max).toBe('');
  });

  // Zero is a bound like any other, which is the whole reason these are null
  // rather than a sentinel number.
  it('treats zero as a real bound', async () => {
    await show([convar({ type: 'float', value: '5', default: '5', min: 0, max: null })]);

    expect((screen.getByDisplayValue('5') as HTMLInputElement).min).toBe('0');
  });
});
