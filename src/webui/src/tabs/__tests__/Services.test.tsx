/*
 * The services list.
 *
 * What is worth checking here is the distinction the page exists to make: a
 * contract is asked for by type and checked by the compiler, a console name is
 * a string nothing checks, and the two are not interchangeable. A view that
 * merged them by default would answer the common question ("what may I put in
 * this constructor") with a list mostly full of convars.
 */

import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ServicesTab } from '../Services';
import type { Service } from '../../types';

vi.mock('../../api', () => ({ getServices: vi.fn() }));

const api = await import('../../api') as unknown as { getServices: ReturnType<typeof vi.fn> };

const contracts: Service[] = [
  { key: 'IGameRules', lookedUpBy: 'type', kind: 'instance', implementation: 'GameRules', built: true },
  { key: 'ISoundscapes', lookedUpBy: 'type', kind: 'instance', implementation: 'Soundscapes', built: true },
  { key: 'IServiceLocator', lookedUpBy: 'type', kind: 'self', implementation: 'ServiceContainer', built: true },
  // A lazy registration nothing has asked for. Not a fault, and the page must
  // not present it as one.
  { key: 'ILater', lookedUpBy: 'type', kind: 'lazy', implementation: 'Later', built: false },
];

const names: Service[] = [
  { key: 'sv_spawn_health', lookedUpBy: 'name', kind: 'convar', implementation: 'SpawnHealthConVar', built: true },
  { key: 'cs_sv_spawn_pickup', lookedUpBy: 'name', kind: 'command', implementation: 'SpawnPickupCommand', built: true },
];

beforeEach(() => {
  vi.clearAllMocks();
  api.getServices.mockResolvedValue({ services: contracts, count: contracts.length });
});

async function show() {
  render(<ServicesTab visible />);
  await screen.findByText('IGameRules');
}

it('lists what a constructor may name, and what answers it', async () => {
  await show();

  const row = within(screen.getByText('IGameRules').closest('tr') as HTMLElement);

  // Both halves. A contract says what may be called; only the implementation
  // says WHICH object arrives, and that is the half that is wrong when an
  // entity consults something that is not the live system.
  expect(row.getByText('GameRules')).toBeTruthy();
  expect(row.getByText('instance')).toBeTruthy();
});

it('asks for contracts by default, not console names', async () => {
  await show();

  expect(api.getServices).toHaveBeenCalledWith('type');
});

it('tells whoever is listening what it loaded', async () => {
  const onLoaded = vi.fn();
  render(<ServicesTab visible onLoaded={onLoaded} />);

  await waitFor(() => expect(onLoaded).toHaveBeenCalledWith(contracts));
});

describe('a lazy service', () => {
  it('is marked as not built yet', async () => {
    await show();

    const row = within(screen.getByText('ILater').closest('tr') as HTMLElement);
    expect(row.getByText('not built yet')).toBeTruthy();
  });

  // The distinction the row has to carry: unbuilt is the ordinary state of
  // something nothing has needed, and reads differently from a failure.
  it('still names the class it would build', async () => {
    await show();

    const row = within(screen.getByText('ILater').closest('tr') as HTMLElement);
    expect(row.getByText('Later')).toBeTruthy();
  });

  it('while a built one says so', async () => {
    await show();

    const row = within(screen.getByText('ISoundscapes').closest('tr') as HTMLElement);
    expect(row.getByText('built')).toBeTruthy();
  });
});

describe('the console names', () => {
  it('are asked for only when the picker says so', async () => {
    await show();

    api.getServices.mockResolvedValue({ services: names, count: names.length });
    await userEvent.selectOptions(screen.getByLabelText('asked for by'), 'name');

    await waitFor(() => expect(api.getServices).toHaveBeenCalledWith('name'));
    expect(await screen.findByText('sv_spawn_health')).toBeTruthy();
  });

  // The trap this note exists to head off: the ConVars tab lists the engine's
  // thousands too, and none of those are in the container.
  it('say that only this mod’s are in the container', async () => {
    await show();

    await userEvent.selectOptions(screen.getByLabelText('asked for by'), 'name');

    expect(await screen.findByText(/the engine.s thousands are/)).toBeTruthy();
  });

  it('are told apart from commands', async () => {
    api.getServices.mockResolvedValue({ services: names, count: names.length });
    render(<ServicesTab visible />);

    const row = within((await screen.findByText('cs_sv_spawn_pickup')).closest('tr') as HTMLElement);
    expect(row.getByText('command')).toBeTruthy();
  });
});

describe('the merged view', () => {
  it('says which key each row is', async () => {
    api.getServices.mockResolvedValue({ services: [...contracts, ...names], count: 6 });
    render(<ServicesTab visible />);

    await screen.findByText('IGameRules');
    await userEvent.selectOptions(screen.getByLabelText('asked for by'), 'all');

    const contract = within((await screen.findByText('IGameRules')).closest('tr') as HTMLElement);
    const name = within((await screen.findByText('sv_spawn_health')).closest('tr') as HTMLElement);

    expect(contract.getByText('type')).toBeTruthy();
    expect(name.getByText('name')).toBeTruthy();
  });

  // In a filtered view every row has the same answer, so the column would be
  // noise rather than information.
  it('and does not, when only one key is showing', async () => {
    await show();

    const row = within(screen.getByText('IGameRules').closest('tr') as HTMLElement);
    expect(row.queryByText('type')).toBeNull();
  });
});

// "Container" appears in no contract name, only in ServiceContainer — so a row
// surviving this filter can only have matched on the class that answers, which
// is the half a plain key filter would miss.
describe('the kind picker', () => {
  it('offers only the kinds actually present', async () => {
    await show();

    const options = within(screen.getByLabelText('kind')).getAllByRole('option');

    expect(options.map(option => option.textContent))
      .toEqual(['any', 'instance', 'lazy', 'self']);
  });

  // The reason it exists: every convar this mod declares is askable by its own
  // class, so the type view is mostly convars and "what systems are there" is
  // hard to see without this.
  it('narrows to one kind', async () => {
    await show();

    await userEvent.selectOptions(screen.getByLabelText('kind'), 'self');

    const table = within(screen.getByRole('table'));

    expect(table.getByText('IServiceLocator')).toBeTruthy();
    expect(table.queryByText('IGameRules')).toBeNull();
  });

  it('and passes everything when unset', async () => {
    await show();

    const table = within(screen.getByRole('table'));

    expect(table.getByText('IGameRules')).toBeTruthy();
    expect(table.getByText('ILater')).toBeTruthy();
  });
});

it('filters on the class that answers, not only the contract', async () => {
  await show();

  await userEvent.type(screen.getByPlaceholderText('filter services'), 'Container');

  const table = within(screen.getByRole('table'));

  expect(table.getByText('IServiceLocator')).toBeTruthy();
  expect(table.queryByText('IGameRules')).toBeNull();
});

// An empty list and a listing the build does not answer are different things,
// and the second must not read as the first.
it('shows an empty container as no matches rather than nothing at all', async () => {
  api.getServices.mockResolvedValue({ services: [], count: 0 });
  render(<ServicesTab visible />);

  expect(await screen.findByText('no services match')).toBeTruthy();
});
