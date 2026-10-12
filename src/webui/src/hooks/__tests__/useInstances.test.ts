/*
 * Which game the page is talking to.
 *
 * The rules here are all about not guessing. Behind a proxy with several games
 * up, picking one silently means showing somebody the logs of a game they were
 * not asking about — and sending every command they run to it. And when the
 * chosen game goes away, the page has to say so: everything on it still reads
 * perfectly and is now a record of something that stopped.
 */

import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import * as api from '../../api';
import { useInstances } from '../useInstances';
import type { Instance } from '../../types';

const instance = (pid: number): Instance =>
  ({ pid, ok: true, map: 'dm_lockdown', address: `/tmp/${pid}.sock` });

const answering = (instances: Instance[] | null): void => {
  vi.spyOn(api, 'listInstances').mockResolvedValue(instances);
};

beforeEach(() => api.setInstancePid(''));
afterEach(() => vi.restoreAllMocks());

const poll = async (result: { current: ReturnType<typeof useInstances> }) =>
  await act(async () => { await result.current[1](); });

describe('with one game', () => {
  // Not even this one is taken without asking. A socket file outlives the
  // process that made it, so the single instance a page can see is sometimes a
  // game that exited half an hour ago — still listed, still answering with its
  // last map, and indistinguishable from a running one until somebody looks.
  it('still asks, because one is not the same as the right one', async () => {
    answering([instance(100)]);

    const { result } = renderHook(() => useInstances(() => {}));
    await poll(result);

    expect(result.current[0].mustChoose).toBe(true);
    expect(result.current[0].selected).toBe('');
    expect(api.getInstancePid()).toBe('');
  });

  it('takes it once it has been chosen, and remembers it', async () => {
    answering([instance(100)]);

    const { result } = renderHook(() => useInstances(() => {}));
    await poll(result);

    act(() => result.current[2]('100'));

    expect(result.current[0].selected).toBe('100');
    expect(result.current[0].mustChoose).toBe(false);
    expect(api.getInstancePid()).toBe('100');

    // And a later poll leaves it alone rather than asking again.
    await poll(result);
    expect(result.current[0].mustChoose).toBe(false);
  });
});

describe('with several', () => {
  it('asks, rather than picking one', async () => {
    answering([instance(100), instance(200)]);

    const { result } = renderHook(() => useInstances(() => {}));
    await poll(result);

    expect(result.current[0].mustChoose).toBe(true);
    expect(result.current[0].selected).toBe('');
    expect(api.getInstancePid()).toBe('');
  });

  it('keeps the one named in the address', async () => {
    history.replaceState(null, '', '/ui?pid=200');
    answering([instance(100), instance(200)]);

    const { result } = renderHook(() => useInstances(() => {}));
    await poll(result);

    expect(result.current[0].selected).toBe('200');
    expect(result.current[0].mustChoose).toBe(false);

    // The request layer has to agree, or the page reads one game and addresses
    // another — which is the failure this whole thing exists to prevent.
    expect(api.getInstancePid()).toBe('200');
  });

  it('puts what was chosen in the address', async () => {
    answering([instance(100), instance(200)]);

    const { result } = renderHook(() => useInstances(() => {}));
    await poll(result);

    act(() => result.current[2]('200'));

    expect(new URLSearchParams(location.search).get('pid')).toBe('200');
    expect(api.getInstancePid()).toBe('200');
  });
});

describe('when the game goes away', () => {
  it('says so, and asks again', async () => {
    history.replaceState(null, '', '/ui?pid=200');
    answering([instance(100), instance(200)]);

    const { result } = renderHook(() => useInstances(() => {}));
    await poll(result);

    answering([instance(100), instance(300)]);
    await poll(result);

    expect(result.current[0].stale).toBe(true);
    expect(result.current[0].mustChoose).toBe(true);
  });

  it('says so even when nothing is left to choose from', async () => {
    history.replaceState(null, '', '/ui?pid=200');
    answering([instance(200)]);

    const { result } = renderHook(() => useInstances(() => {}));
    await poll(result);

    answering([]);
    await poll(result);

    expect(result.current[0].stale).toBe(true);
  });

  // A page served by a game directly has no proxy and no picker; it is not
  // stale, it simply never had a choice to make.
  it('is not stale when there was never a proxy', async () => {
    answering(null);

    const { result } = renderHook(() => useInstances(() => {}));
    await poll(result);

    expect(result.current[0].stale).toBe(false);
    expect(result.current[0].instances).toBeNull();
  });
});
