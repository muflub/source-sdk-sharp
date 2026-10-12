/*
 * The inspector.
 *
 * The behaviour worth pinning down is what changes between the two modes: in
 * static the list is the map, so everything is editable and the edits are what
 * get written; in live it is the engine's, and the only way to change a value is
 * to tell the running game.
 */

import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { Inspector } from '../Inspector';
import { fromMap, relink, type Entity } from '../model';
import type { MapEntity } from '../../types';

vi.mock('../../api', () => ({ fireInput: vi.fn(), fireOutput: vi.fn() }));

const api = await import('../../api') as unknown as {
  fireInput: ReturnType<typeof vi.fn>;
  fireOutput: ReturnType<typeof vi.fn>;
};

const entity = (over: Partial<MapEntity>): Entity => fromMap({
  id: 0, classname: 'logic_relay', targetname: 'plug_pull', model: '', brushModel: -1,
  origin: [1, 2, 3], angles: [0, 0, 0], keyvalues: [], connections: [], ...over,
});

const wire = (target: string, input = 'Kill') =>
  ({ output: 'OnTrigger', target, input, param: '', delay: 0, times: -1 });

beforeEach(() => {
  vi.clearAllMocks();
  api.fireInput.mockResolvedValue({ ok: true });
  api.fireOutput.mockResolvedValue({ ok: true });
});

function show(target: Entity | null, mode: 'static' | 'live' = 'static') {
  const onChanged = vi.fn();
  const onRemove = vi.fn();
  const onSelectByName = vi.fn();

  render(
    <Inspector
      entity={target}
      mode={mode}
      map="dm_lockdown"
      onChanged={onChanged}
      onRemove={onRemove}
      onSelectByName={onSelectByName}
      onResult={() => {}}
    />);

  return { onChanged, onRemove, onSelectByName };
}

it('says so when nothing is selected', () => {
  show(null);
  expect(screen.getByText(/nothing selected/)).toBeTruthy();
});

describe('in static mode', () => {
  it('lets the name, class and position be edited', async () => {
    const target = entity({});
    const { onChanged } = show(target);

    // Held rather than re-queried: once it is cleared there are several empty
    // boxes and the query would be ambiguous.
    const name = screen.getByDisplayValue('plug_pull');

    await userEvent.clear(name);
    await userEvent.type(name, 'renamed');

    expect(target.targetname).toBe('renamed');
    expect(onChanged).toHaveBeenCalled();
  });

  it('offers to delete the entity', async () => {
    const target = entity({});
    const { onRemove } = show(target);

    await userEvent.click(screen.getByRole('button', { name: 'Delete this entity' }));
    expect(onRemove).toHaveBeenCalledWith(target);
  });

  // In static mode a connection is part of the map, so removing it is an edit.
  it('removes a connection from the map rather than firing it', async () => {
    const target = entity({ connections: [wire('door')] });
    const { onChanged } = show(target);

    await userEvent.click(screen.getByTitle('remove this connection from the map'));

    expect(target.connections).toHaveLength(0);
    expect(onChanged).toHaveBeenCalled();
    expect(api.fireInput).not.toHaveBeenCalled();
  });

  it('adds a key, and replaces one that already exists', async () => {
    const target = entity({ keyvalues: [{ key: 'speed', value: '100' }] });
    show(target);

    await userEvent.type(screen.getByPlaceholderText('key'), 'speed');
    await userEvent.type(screen.getByPlaceholderText('value'), '250');
    await userEvent.click(screen.getByRole('button', { name: 'Add key' }));

    expect(target.keyvalues).toHaveLength(1);
    expect(target.keyvalues[0]!.value).toBe('250');
  });
});

describe('in live mode', () => {
  it('sends a keyvalue to the running entity instead of editing it', async () => {
    show(entity({ keyvalues: [{ key: 'speed', value: '100' }] }), 'live');

    await userEvent.click(screen.getByRole('button', { name: 'set' }));

    expect(api.fireInput).toHaveBeenCalledWith('plug_pull', 'AddOutput', 'speed 100', 0);
  });

  it('fires a single connection', async () => {
    show(entity({ connections: [wire('door', 'Open')] }), 'live');

    await userEvent.click(screen.getByRole('button', { name: 'send' }));

    expect(api.fireInput).toHaveBeenCalledWith('door', 'Open', '', 0);
  });
});

describe('outputs', () => {
  // Grouped by output, because that is the unit a person thinks in: "what does
  // OnTrigger do" rather than "what is the fourth connection".
  it('are grouped, and the group fires all of them', async () => {
    show(entity({ connections: [wire('a'), wire('b')] }), 'live');

    const fire = screen.getByRole('button', { name: 'Fire 2' });
    await userEvent.click(fire);

    expect(api.fireOutput).toHaveBeenCalledWith('plug_pull', 'OnTrigger', 'dm_lockdown');
  });

  it('link to what they point at', async () => {
    const { onSelectByName } = show(entity({ connections: [wire('door')] }));

    await userEvent.click(screen.getByRole('button', { name: 'door' }));
    expect(onSelectByName).toHaveBeenCalledWith('door');
  });
});

describe('reverse edges', () => {
  it('say what fires this entity', () => {
    const button = entity({ id: 0, targetname: 'button', connections: [wire('plug_pull', 'Trigger')] });
    const target = entity({ id: 1 });

    relink([button, target]);
    show(target);

    expect(within(screen.getByText('Fired by').closest('section')!)
      .getByRole('button', { name: 'button' })).toBeTruthy();
  });
});

// A connection is stored as a keyvalue in the map file, and it is already shown
// under Outputs. Showing it twice invites an edit to one copy that the other
// silently overwrites on the way out.
it('does not list connections among the keyvalues', () => {
  show(entity({
    keyvalues: [
      { key: 'OnTrigger', value: 'door,Open,,0,-1' },
      { key: 'speed', value: '100' },
    ],
    connections: [wire('door', 'Open')],
  }));

  const keys = screen.getByText('Keyvalues').closest('section')!;

  expect(within(keys).getByText('speed')).toBeTruthy();
  expect(within(keys).queryByText('OnTrigger')).toBeNull();
});

// ent_fire addresses by name; without one there is nothing to aim at.
it('does not offer to fire at an entity with no name', () => {
  show(entity({ targetname: '' }), 'live');
  expect(screen.getByText(/no targetname/)).toBeTruthy();
});
