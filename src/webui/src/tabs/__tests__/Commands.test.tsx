/*
 * The command list.
 *
 * Run and Edit are the whole surface, and they are deliberately different: a
 * command that takes arguments and one that does not are used in completely
 * different ways, so one fires immediately and the other primes the console.
 */

import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { CommandsTab } from '../Commands';
import type { ConCommand } from '../../types';

vi.mock('../../api', () => ({ getCommands: vi.fn() }));

const api = await import('../../api') as unknown as { getCommands: ReturnType<typeof vi.fn> };

const commands: ConCommand[] = [
  { name: 'cs_spawn_pickup', help: 'Spawn a pickup', declaredBy: 'SpawnPickupCommand', flags: '', registered: true, source: 'managed' },
  { name: 'cs_reset_rules', help: 'Put the game rules back', declaredBy: 'ResetRulesCommand', flags: '', registered: true, source: 'managed' },
];

/** One of the engine's, which is a different row in every way that matters. */
const native: ConCommand = {
  name: 'changelevel',
  help: 'Change server to the specified map',
  declaredBy: 'server.dll',
  flags: 'GameDll, Cheat',
  registered: true,
  source: 'native',
};

beforeEach(() => {
  vi.clearAllMocks();
  api.getCommands.mockResolvedValue({ commands, count: commands.length });
});

async function show(props: Partial<Parameters<typeof CommandsTab>[0]> = {}) {
  const onRun = vi.fn();
  const onPrime = vi.fn();

  render(<CommandsTab visible onRun={onRun} onPrime={onPrime} {...props} />);
  await screen.findByText('cs_spawn_pickup');

  return { onRun, onPrime };
}

it('lists what the mod registered, with where it came from', async () => {
  await show();

  expect(screen.getByText('cs_spawn_pickup')).toBeTruthy();
  expect(screen.getByText('SpawnPickupCommand')).toBeTruthy();
});

// A command the engine would not take. The class exists and was built, so it is
// in the listing — but nothing a player types reaches it, and a debugging tool
// offering a Run button for one has set exactly the trap the flags column
// exists to avoid.
describe('a command the engine refused', () => {
  const refused: ConCommand = {
    name: 'cs_refused',
    help: 'a name the engine already had',
    declaredBy: 'RefusedCommand',
    flags: '',
    registered: false,
    source: 'managed',
  };

  beforeEach(() => {
    api.getCommands.mockResolvedValue({ commands: [...commands, refused], count: 3 });
  });

  it('says so on the row', async () => {
    await show();

    expect(screen.getByText(/the engine refused this command/)).toBeTruthy();
  });

  it('and offers neither button', async () => {
    await show();

    const row = within(screen.getByText('cs_refused').closest('tr') as HTMLElement);

    expect(row.getByRole('button', { name: 'Run' }).hasAttribute('disabled')).toBe(true);
    expect(row.getByRole('button', { name: 'Edit' }).hasAttribute('disabled')).toBe(true);
  });

  it('while a registered one still offers both', async () => {
    await show();

    const row = within(screen.getByText('cs_spawn_pickup').closest('tr') as HTMLElement);

    expect(row.getByRole('button', { name: 'Run' }).hasAttribute('disabled')).toBe(false);
  });
});

it('tells whoever is listening what it loaded', async () => {
  const onLoaded = vi.fn();
  await show({ onLoaded });

  expect(onLoaded).toHaveBeenCalledWith(commands);
});

describe('the buttons', () => {
  const rowFor = (name: string) =>
    within(screen.getByText(name).closest('tr') as HTMLElement);

  it('Run fires the command as it stands', async () => {
    const { onRun, onPrime } = await show();

    await userEvent.click(rowFor('cs_spawn_pickup').getByRole('button', { name: 'Run' }));

    expect(onRun).toHaveBeenCalledWith('cs_spawn_pickup');
    expect(onPrime).not.toHaveBeenCalled();
  });

  // With a trailing space, because the point of Edit is to type arguments next.
  it('Edit primes the console instead of running anything', async () => {
    const { onRun, onPrime } = await show();

    await userEvent.click(rowFor('cs_reset_rules').getByRole('button', { name: 'Edit' }));

    expect(onPrime).toHaveBeenCalledWith('cs_reset_rules ');
    expect(onRun).not.toHaveBeenCalled();
  });
});

describe('filtering', () => {
  it('matches the name', async () => {
    await show();
    await userEvent.type(screen.getByPlaceholderText('filter commands'), 'pickup');

    // In the table specifically: the filter box suggests the same name while it
    // is being typed, and that is not what this is asking about.
    expect(screen.getByRole('cell', { name: 'cs_spawn_pickup' })).toBeTruthy();
    expect(screen.queryByRole('cell', { name: 'cs_reset_rules' })).toBeNull();
  });

  it('matches the help text too', async () => {
    await show();
    await userEvent.type(screen.getByPlaceholderText('filter commands'), 'rules back');

    expect(screen.getByText('cs_reset_rules')).toBeTruthy();
    expect(screen.queryByText('cs_spawn_pickup')).toBeNull();
  });

  it('says so when nothing matches', async () => {
    await show();
    await userEvent.type(screen.getByPlaceholderText('filter commands'), 'zzz');

    expect(screen.getByText('no commands match')).toBeTruthy();
  });
});

describe('sorting', () => {
  const rows = (): string[] => screen.getAllByRole('row').slice(1)
    .map(row => (row as HTMLTableRowElement).cells[0]!.textContent ?? '');

  it('starts by name, and turns around when that column is clicked', async () => {
    await show();

    expect(rows()).toEqual(['cs_reset_rules', 'cs_spawn_pickup']);

    await userEvent.click(screen.getByRole('button', { name: /^Command/ }));
    expect(rows()).toEqual(['cs_spawn_pickup', 'cs_reset_rules']);

    await userEvent.click(screen.getByRole('button', { name: /^Command/ }));
    expect(rows()).toEqual(['cs_reset_rules', 'cs_spawn_pickup']);
  });

  it('orders by where a command was declared', async () => {
    await show();

    await userEvent.click(screen.getByRole('button', { name: /^Declared by/ }));

    // Sorted by the declaring class, which is what groups a mod's commands by
    // the thing that owns them.
    expect(rows()).toEqual(['cs_reset_rules', 'cs_spawn_pickup']);
  });
});

it('says when the list was read', async () => {
  await show();

  expect(screen.getByText(/^read at /)).toBeTruthy();
});

/*
 * The engine's commands, once the source picker asks for them. The flags are
 * the reason they are worth listing: a command marked Cheat does nothing at all
 * without sv_cheats, and says nothing when it does nothing.
 */
describe('a native command', () => {
  it('is listed with its flags and the DLL that registered it', async () => {
    api.getCommands.mockResolvedValue({ commands: [native], count: 1 });

    render(<CommandsTab visible onRun={vi.fn()} onPrime={vi.fn()} />);
    await screen.findByText('changelevel');

    const row = within(screen.getByText('changelevel').closest('tr') as HTMLElement);
    expect(row.getByText('server.dll')).toBeTruthy();
    expect(row.getByText('Cheat')).toBeTruthy();

    // Nothing appended to the name: where it came from has a column.
    expect(screen.getByRole('cell', { name: 'changelevel' })).toBeTruthy();
  });

  it('is asked for only when the source picker says so', async () => {
    await show();
    expect(api.getCommands).toHaveBeenCalledWith('managed');

    await userEvent.selectOptions(screen.getByLabelText('source'), 'native');

    await waitFor(() => expect(api.getCommands).toHaveBeenCalledWith('native'));
  });
});

describe('filtering by flag', () => {
  it('keeps only the commands carrying it', async () => {
    api.getCommands.mockResolvedValue({ commands: [...commands, native], count: 3 });

    render(<CommandsTab visible onRun={vi.fn()} onPrime={vi.fn()} />);
    await screen.findByText('changelevel');

    await userEvent.selectOptions(screen.getByLabelText('flag'), 'Cheat');

    expect(screen.getByText('changelevel')).toBeTruthy();
    expect(screen.queryByText('cs_spawn_pickup')).toBeNull();
  });

  // This mod's commands are registered with no flags at all, which is a
  // different thing from an empty set of them and filters as one.
  it('finds the ones with no flags', async () => {
    api.getCommands.mockResolvedValue({ commands: [...commands, native], count: 3 });

    render(<CommandsTab visible onRun={vi.fn()} onPrime={vi.fn()} />);
    await screen.findByText('changelevel');

    await userEvent.selectOptions(screen.getByLabelText('flag'), 'none');

    expect(screen.getByText('cs_spawn_pickup')).toBeTruthy();
    expect(screen.queryByText('changelevel')).toBeNull();
  });
});
