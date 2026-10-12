/*
 * Which game is being talked to.
 *
 * Only meaningful behind the proxy: a game serving this page itself is the only
 * one there is, and the picker stays hidden. Null means no proxy at all, which
 * is a different thing from a proxy that knows of no games.
 *
 * The choice lives in the address, so a tab is a link to a particular game and
 * a reload comes back to the same one. Nothing is ever chosen for you — not even
 * when only one game is up. A page that silently picked one would have you
 * reading the logs of a game you were not asking about, and "there is only one"
 * is exactly the case where that goes unnoticed: a socket file outlives the
 * process that made it, so the only instance the page can see is sometimes a
 * game that exited half an hour ago, still answering with its last map.
 *
 * With one running it is one click, once, and the address remembers it.
 */

import { useCallback, useState } from 'react';

import { getInstancePid, listInstances, setInstancePid } from '../api';
import type { Instance } from '../types';
import { readParam, writeParam } from './useUrlState';

/** The query parameter naming the game, so a link can carry it. */
export const PID_PARAM = 'pid';

export interface InstancesState {
  instances: Instance[] | null;
  selected: string;

  /** Several are up and none is named: the page has to ask. */
  mustChoose: boolean;

  /**
   * The game this page was watching has gone.
   *
   * Kept apart from "no instances yet": a page that never had one is starting
   * up, and a page whose game has exited is showing a corpse — every list on it
   * is a record of something that is no longer running.
   */
  stale: boolean;
}

export function useInstances(onSwitched: () => void): [InstancesState, () => Promise<void>, (pid: string) => void] {
  const [state, setState] = useState<InstancesState>({
    instances: null, selected: readParam(PID_PARAM), mustChoose: false, stale: false,
  });

  const choose = useCallback((pid: string, switched: boolean): void => {
    setInstancePid(pid);
    writeParam(PID_PARAM, pid);

    setState(previous => ({ ...previous, selected: pid, mustChoose: false, stale: false }));

    if (switched) onSwitched();
  }, [onSwitched]);

  const poll = useCallback(async (): Promise<void> => {
    const instances = await listInstances();

    // No proxy, or a proxy that knows of no games. Either way there is nothing
    // to choose between and nothing to say about staleness — the game serving
    // this page directly answers for itself.
    if (instances === null || instances.length === 0) {
      setState(previous => ({
        instances: null,
        selected: getInstancePid(),
        mustChoose: false,

        // Only if we had one and it is gone. A proxy with nothing behind it
        // that we never picked from is just an empty picker.
        stale: previous.instances !== null && previous.selected !== '',
      }));

      return;
    }

    const named = readParam(PID_PARAM) || getInstancePid();
    const known = instances.some(instance => String(instance.pid) === named);

    // Named and still up: keep it, and make sure the request layer agrees — a
    // page opened on a link has the pid in the address before anything has run.
    if (named && known) {
      if (getInstancePid() !== named) setInstancePid(named);
      setState({ instances, selected: named, mustChoose: false, stale: false });
      return;
    }

    // Nothing named, or what was named has gone. Either way this is a question,
    // and it stays a question when there is only one answer available: the one
    // instance in the list may be a crashed game's leftover socket, and picking
    // it silently is how a page ends up showing a map that stopped running.
    setState({
      instances,
      selected: '',
      mustChoose: true,
      stale: named !== '',
    });
  }, [onSwitched]);

  const select = useCallback((pid: string): void => choose(pid, true), [choose]);

  return [state, poll, select];
}
