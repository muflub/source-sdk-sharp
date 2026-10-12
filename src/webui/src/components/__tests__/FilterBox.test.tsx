/*
 * The filter box that suggests.
 *
 * What matters is when it stays quiet. A list that opens on focus with every
 * name the game has is a list nobody reads, and it covers the rows underneath —
 * which on this page are the thing being filtered.
 */

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { expect, it, vi } from 'vitest';

import { FilterBox } from '../FilterBox';

const NAMES = ['cs_sv_spawn_health', 'cs_sv_spawn_protect', 'cs_sv_fraglimit', 'mp_teamplay'];

function show(onChange = vi.fn()) {
  function Host(): React.ReactNode {
    const [value, setValue] = useState('');

    return (
      <FilterBox
        value={value}
        options={NAMES}
        placeholder="filter convars"
        onChange={next => { setValue(next); onChange(next); }}
      />
    );
  }

  render(<Host />);
  return { box: screen.getByPlaceholderText('filter convars'), onChange };
}

const suggestions = (): string[] =>
  screen.queryAllByRole('option').map(option => option.textContent ?? '');

it('says nothing until enough has been typed to mean something', async () => {
  const { box } = show();

  await userEvent.click(box);
  expect(suggestions()).toHaveLength(0);

  await userEvent.type(box, 'sp');
  expect(suggestions()).toHaveLength(0);

  await userEvent.type(box, 'a');
  expect(suggestions().length).toBeGreaterThan(0);
});

it('puts what starts with the text first', async () => {
  const { box } = show();

  await userEvent.type(box, 'cs_sv_spawn');

  expect(suggestions()).toEqual(['cs_sv_spawn_health', 'cs_sv_spawn_protect']);
});

it('offers what merely contains it too', async () => {
  const { box } = show();

  await userEvent.type(box, 'frag');

  expect(suggestions()).toEqual(['cs_sv_fraglimit']);
});

it('filters by what was picked', async () => {
  const { box, onChange } = show();

  await userEvent.type(box, 'frag');
  await userEvent.click(screen.getByRole('option', { name: 'cs_sv_fraglimit' }));

  expect(onChange).toHaveBeenLastCalledWith('cs_sv_fraglimit');

  // And then closes: the filter has arrived, and the list would be covering the
  // rows it just found.
  expect(suggestions()).toHaveLength(0);
});

it('closes on escape without changing the filter', async () => {
  const { box, onChange } = show();

  await userEvent.type(box, 'spawn');
  expect(suggestions().length).toBeGreaterThan(0);

  await userEvent.type(box, '{Escape}');

  expect(suggestions()).toHaveLength(0);
  expect(onChange).toHaveBeenLastCalledWith('spawn');
});

it('can be driven from the keyboard', async () => {
  const { box, onChange } = show();

  await userEvent.type(box, 'spawn');
  await userEvent.type(box, '{ArrowDown}{Enter}');

  expect(onChange).toHaveBeenLastCalledWith('cs_sv_spawn_protect');
});
