/*
 * The scripted entity tab.
 *
 * What matters here is that it reports the registration faithfully — the fire
 * counts especially, since "did this connection ever run, and with what" is the
 * question a map that misbehaves actually asks, and a connection that never ran
 * looks exactly like one that ran and did nothing.
 */

import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ScriptedTab } from '../Scripted';
import type { ScriptedClass, ScriptedInstance } from '../../types';

vi.mock('../../api', () => ({ getEntityClasses: vi.fn(), getInstances: vi.fn() }));

const api = await import('../../api') as unknown as {
  getEntityClasses: ReturnType<typeof vi.fn>;
  getInstances: ReturnType<typeof vi.fn>;
};

const timer: ScriptedClass = {
  className: 'cs_timer',
  kind: 'Logical',
  declaredBy: 'TimerEntity',
  liveCount: 1,
  inputs: ['Start', 'Stop'],
  outputs: ['OnTimer'],
  vars: [{ name: 'interval', type: 'float' }],
};

const instance: ScriptedInstance = {
  className: 'cs_timer',
  handle: 42,
  index: 7,
  valid: true,
  origin: [1, 2, 3],
  health: 100,
  spawnFlags: 0,
  vars: [{ name: 'interval', type: 'float', value: '0.5' }],
  outputs: [{ name: 'OnTimer', fireCount: 3, lastValue: 'fired' }],
};

beforeEach(() => {
  vi.clearAllMocks();
  api.getEntityClasses.mockResolvedValue({ classes: [timer], count: 1 });
  api.getInstances.mockResolvedValue({ instances: [instance], count: 1 });
});

const show = async () => {
  render(<ScriptedTab visible />);
  await screen.findByRole('heading', { name: 'cs_timer' });
};

describe('a declared class', () => {
  it('shows what a map may send it and what it sends back', async () => {
    await show();

    const card = screen.getByRole('heading', { name: 'cs_timer' }).closest('.card') as HTMLElement;

    expect(within(card).getByText('Start')).toBeTruthy();
    expect(within(card).getByText('OnTimer')).toBeTruthy();
    expect(within(card).getByText('interval: float')).toBeTruthy();
  });

  it('says where it came from and how many are live', async () => {
    await show();
    expect(screen.getByText(/Logical · TimerEntity · 1 live/)).toBeTruthy();
  });

  it('says "none" rather than nothing for a class with no outputs', async () => {
    api.getEntityClasses.mockResolvedValue({
      classes: [{ ...timer, inputs: [], outputs: [], vars: [] }], count: 1,
    });

    await show();

    const card = screen.getByRole('heading', { name: 'cs_timer' }).closest('.card') as HTMLElement;
    expect(within(card).getAllByText('none')).toHaveLength(3);
  });
});

describe('a live instance', () => {
  it('totals what its outputs have fired', async () => {
    await show();

    const row = screen.getAllByText('cs_timer').find(n => n.tagName === 'TD')!.closest('tr') as HTMLElement;
    expect(within(row).getByText('3')).toBeTruthy();
  });

  it('opens to show its keys and what each output did', async () => {
    await show();
    await userEvent.click(screen.getByRole('button', { name: '▸' }));

    expect(screen.getByText('interval')).toBeTruthy();
    expect(screen.getByText('0.5')).toBeTruthy();
    expect(screen.getByText(/3×.*last: fired/)).toBeTruthy();
  });

  // An entity removed under us stays listed until the level ends. Dropping it
  // would be less useful than showing it — one that vanished when it should not
  // have is usually the thing being looked for.
  it('is still listed once it has been removed, marked invalid', async () => {
    api.getInstances.mockResolvedValue({
      instances: [{ ...instance, valid: false, index: -1 }], count: 1,
    });

    await show();

    expect(screen.getByText('removed')).toBeTruthy();
  });
});

it('does not ask for instances while the tab is hidden', async () => {
  render(<ScriptedTab visible={false} />);
  await screen.findByRole('heading', { name: 'cs_timer' });

  expect(api.getInstances).not.toHaveBeenCalled();
});
