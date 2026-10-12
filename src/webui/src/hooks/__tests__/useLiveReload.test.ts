/*
 * Reloading the page when the interface is rebuilt.
 *
 * The rules worth pinning down are the ones about not reloading: not on the
 * first answer, not when the build cannot change under the page, and not while
 * the map tab is holding edits that exist nowhere else.
 */

import { renderHook, act } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import * as api from '../../api';
import { useLiveReload } from '../useLiveReload';

const reload = vi.fn();

beforeEach(() => {
  reload.mockClear();

  // jsdom's location.reload cannot be called, so it is replaced outright — the
  // whole question here is whether it would have been.
  Object.defineProperty(window, 'location', {
    configurable: true,
    value: { ...window.location, reload },
  });
});

afterEach(() => vi.restoreAllMocks());

const answering = (...stamps: string[]): void => {
  const queue = [...stamps];
  vi.spyOn(api, 'getUiStamp').mockImplementation(async () =>
    ({ stamp: queue.length > 1 ? queue.shift()! : queue[0]!, live: true }));
};

describe('a rebuilt interface', () => {
  it('reloads the page when the stamp changes', async () => {
    answering('one', 'two');

    const { result } = renderHook(() => useLiveReload(() => true));

    await act(async () => { await result.current[1](); });
    expect(reload).not.toHaveBeenCalled();     // the first answer is the baseline

    await act(async () => { await result.current[1](); });
    expect(reload).toHaveBeenCalled();
  });

  it('does nothing while the stamp is the same', async () => {
    answering('one');

    const { result } = renderHook(() => useLiveReload(() => true));

    await act(async () => { await result.current[1](); });
    await act(async () => { await result.current[1](); });

    expect(reload).not.toHaveBeenCalled();
  });

  // The map tab's edits exist nowhere but the page until they are saved.
  it('offers the reload rather than taking it when something is unsaved', async () => {
    answering('one', 'two');

    const { result } = renderHook(() => useLiveReload(() => false));

    await act(async () => { await result.current[1](); });
    await act(async () => { await result.current[1](); });

    expect(reload).not.toHaveBeenCalled();
    expect(result.current[0]).toBe(true);
  });

  // A release build serves the page from inside the assembly, where it cannot
  // change; asking is harmless but acting on the answer would not be.
  it('ignores a build that cannot change under it', async () => {
    const stamps = ['one', 'two'];
    vi.spyOn(api, 'getUiStamp').mockImplementation(async () =>
      ({ stamp: stamps.shift() ?? 'two', live: false }));

    const { result } = renderHook(() => useLiveReload(() => true));

    await act(async () => { await result.current[1](); });
    await act(async () => { await result.current[1](); });

    expect(reload).not.toHaveBeenCalled();
    expect(result.current[0]).toBe(false);
  });

  it('says nothing when the game does not answer', async () => {
    vi.spyOn(api, 'getUiStamp').mockResolvedValue(null);

    const { result } = renderHook(() => useLiveReload(() => true));
    await act(async () => { await result.current[1](); });

    expect(reload).not.toHaveBeenCalled();
  });
});
