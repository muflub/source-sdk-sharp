/*
 * The game's status, and whether it is answering at all.
 *
 * Separate from the poll loop that drives it so a component can read the answer
 * without knowing how often it is asked for.
 */

import { useState } from 'react';
import { getStatus } from '../api';
import type { Status } from '../types';

export type Connection = 'waiting' | 'live' | 'lost';

export interface StatusState {
  status: Status | null;
  connection: Connection;
  detail: string;
}

export function useStatus(): [StatusState, () => Promise<void>] {
  const [state, setState] = useState<StatusState>({
    status: null, connection: 'waiting', detail: 'connecting',
  });

  const poll = async (): Promise<void> => {
    try {
      const status = await getStatus();

      // An error in the body is a game that answered and said no — which is a
      // different thing from one that did not answer, and reads differently.
      setState(status.error
        ? { status: null, connection: 'lost', detail: status.error }
        : { status, connection: 'live', detail: 'live' });
    } catch {
      setState({ status: null, connection: 'lost', detail: 'no answer' });
    }
  };

  return [state, poll];
}
