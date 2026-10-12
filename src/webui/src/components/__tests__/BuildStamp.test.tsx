/*
 * Which build the game is running, in the bar.
 *
 * What matters is that it cannot be quietly wrong. Three ways it could be:
 * showing one number where two halves disagree, saying nothing when the game is
 * running code that has since been rebuilt, and letting a build made from a
 * dirty tree read as a clean commit. All three are provoked here.
 */

import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { expect, it } from 'vitest';

import { BuildStamp } from '../BuildStamp';
import { advice, dirtiness, outOfDate, short, sourceDiverged } from '../build';
import type { Build, BuildHalf } from '../../types';

const NOON = Date.UTC(2026, 7, 18, 12, 0, 0);

function half(over: Partial<BuildHalf> = {}): BuildHalf {
  return {
    known: true,
    hash: 'a1b2c3d4',
    at: NOON,
    parts: 6,
    branch: 'example-trunk-branch',
    commit: '45e801f2',
    dirty: false,
    source: 'example-trunk-branch@45e801f2',
    freshness: 'current',
    path: '/mod/bin/linux64/server.so',
    ...over,
  };
}

function build(over: Partial<Build> = {}): Build {
  return { native: half(), managed: half({ hash: 'e5f6a7b8' }), ...over };
}

//---------------------------------------------------------------------------
// The bar.
//---------------------------------------------------------------------------

it('shows both halves, because a reload moves only one of them', () => {
  render(<BuildStamp build={build()} />);

  const line = screen.getByRole('button');

  expect(line.textContent).toContain('a1b2c3d4');
  expect(line.textContent).toContain('e5f6a7b8');
  expect(line.textContent).toContain('example-trunk-branch');
});

it('says nothing at all for a game too old to carry a stamp', () => {
  const { container } = render(<BuildStamp build={undefined} />);

  expect(container.textContent).toBe('');
});

it('marks a game whose code has been rebuilt under it', () => {
  const fresh = render(<BuildStamp build={build()} />);
  expect(fresh.container.querySelector('.build')?.getAttribute('data-stale')).toBe('no');
  expect(fresh.container.textContent).not.toContain('rebuilt');

  fresh.unmount();

  const stale = render(<BuildStamp build={build({ managed: half({ freshness: 'stale' }) })} />);
  expect(stale.container.querySelector('.build')?.getAttribute('data-stale')).toBe('yes');
  expect(stale.container.textContent).toContain('rebuilt');
});

it('marks two halves built from different source', () => {
  render(<BuildStamp build={build({
    managed: half({ source: 'example-feature-a1b2c3@45e801f2+dirty', branch: 'example-feature-a1b2c3' }),
  })} />);

  expect(screen.getByRole('button').textContent).toContain('two trees');
});

//---------------------------------------------------------------------------
// The panel behind it.
//---------------------------------------------------------------------------

it('opens the detail on click and closes it again', async () => {
  render(<BuildStamp build={build()} />);

  expect(screen.queryByRole('dialog')).toBeNull();

  await userEvent.click(screen.getByRole('button'));
  expect(screen.getByRole('dialog')).not.toBeNull();

  await userEvent.click(screen.getByRole('button', { name: /example-trunk-branch/ }));
  expect(screen.queryByRole('dialog')).toBeNull();
});

it('shows the two halves side by side', async () => {
  render(<BuildStamp build={build()} />);
  await userEvent.click(screen.getByRole('button'));

  const pane = screen.getByRole('dialog');

  expect(within(pane).getByText('native')).not.toBeNull();
  expect(within(pane).getByText('managed')).not.toBeNull();
  expect(pane.textContent).toContain('e5f6a7b8');
});

it('tells the reader which thing to do about each half', async () => {
  render(<BuildStamp build={build({
    native: half({ freshness: 'stale' }),
    managed: half({ hash: 'e5f6a7b8', freshness: 'stale' }),
  })} />);

  await userEvent.click(screen.getByRole('button'));

  const pane = screen.getByRole('dialog');

  // Not interchangeable: native cannot be reloaded into a running process, so
  // offering cs_sv_reload for it would be advice that silently does nothing.
  expect(pane.textContent).toContain('restart it');
  expect(pane.textContent).toContain('cs_sv_reload');
});

it('shows a half nothing stamped as unstamped rather than as an empty build', async () => {
  render(<BuildStamp build={build({
    native: { ...half(), known: false, hash: '', at: 0, parts: 0, source: 'unknown', dirty: null },
  })} />);

  await userEvent.click(screen.getByRole('button'));

  expect(screen.getByRole('dialog').textContent).toContain('Nothing stamped this half');
});

//---------------------------------------------------------------------------
// The rules underneath, each provoked in both directions.
//---------------------------------------------------------------------------

it('never renders an unknown tree as a clean one', () => {
  expect(dirtiness(half({ dirty: false }))).toBe('clean');
  expect(dirtiness(half({ dirty: true }))).toBe('uncommitted work');

  // The one that matters: git could not be asked. Saying "clean" here would be
  // a build with uncommitted work presenting itself as a named commit.
  expect(dirtiness(half({ dirty: null }))).toBe('could not be asked');
  expect(dirtiness(half({ known: false, source: 'unknown' }))).toBe('not known');
});

it('gives different advice for the two halves and none when neither is stale', () => {
  expect(advice('native', 'stale')).toContain('restart');
  expect(advice('managed', 'stale')).toContain('cs_sv_reload');
  expect(advice('native', 'stale')).not.toContain('cs_sv_reload');

  expect(advice('native', 'current')).toBe('');
  expect(advice('managed', 'current')).toBe('');
  expect(advice('managed', 'unknown')).toBe('');
});

it('calls a game out of date only when something under it moved', () => {
  expect(outOfDate(build())).toBe(false);
  expect(outOfDate(undefined)).toBe(false);
  expect(outOfDate(build({ native: half({ freshness: 'stale' }) }))).toBe(true);
  expect(outOfDate(build({ managed: half({ freshness: 'stale' }) }))).toBe(true);
  expect(outOfDate(build({ managed: half({ freshness: 'unknown' }) }))).toBe(false);
});

it('does not call two unstamped halves a divergence', () => {
  const nothing = { ...half(), known: false, source: 'unknown' };

  expect(sourceDiverged(build({ native: nothing, managed: nothing }))).toBe(false);
  expect(sourceDiverged(build({ native: nothing }))).toBe(false);
  expect(sourceDiverged(build())).toBe(false);
  expect(sourceDiverged(build({ managed: half({ source: 'other@abcdef01' }) }))).toBe(true);
});

it('reads a stamp as a time and a hash, and an unstamped half as unknown', () => {
  expect(short(half({ hash: 'a1b2c3d4' }))).toContain('a1b2c3d4');
  expect(short(half({ known: false }))).toBe('unknown');

  // Two builds an hour apart do not read alike, which is the whole point.
  expect(short(half())).not.toBe(short(half({ at: NOON + 3600_000 })));
});
