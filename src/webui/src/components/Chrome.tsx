/*
 * The bar across the top, the tabs under it, and the console at the bottom.
 *
 * The parts of the page that belong to no tab. They were the first thing worth
 * converting because everything else hangs off them: which tab is showing, which
 * game is being talked to, and whether it is answering.
 */

import type { FormEvent, ReactNode, RefObject } from 'react';
import type { Connection, StatusState } from '../hooks/useStatus';
import type { InstancesState } from '../hooks/useInstances';
import { BuildStamp } from './BuildStamp';

export const TABS = ['logs', 'commands', 'convars', 'services', 'map', 'mapfile', 'scripted', 'nettables', 'prediction', 'mockups'] as const;
export type Tab = (typeof TABS)[number];

const TAB_LABELS: Record<Tab, string> = {
  logs: 'Logs',
  commands: 'Commands',
  convars: 'ConVars',
  services: 'Services',
  map: 'Entities',
  mapfile: 'Map file',
  scripted: 'Scripted',
  nettables: 'Net tables',
  prediction: 'Prediction',
  mockups: 'Mockups',
};

export function Header({ status, instances, rebuilt, onSelectInstance }: {
  status: StatusState;
  instances: InstancesState;

  /** A new build of this page is on disk, and something is unsaved here. */
  rebuilt: boolean;
  onSelectInstance: (pid: string) => void;
}): ReactNode {
  const chips: [string, string | number][] = [];
  const game = status.status;

  if (game?.map) chips.push(['map', game.map]);
  if (game?.ticks !== undefined) chips.push(['tick', game.ticks]);
  if (game?.time !== undefined) chips.push(['time', `${game.time.toFixed(2)}s`]);
  if (game?.pid !== undefined) chips.push(['pid', game.pid]);

  return (
    <header className="bar">
      <span className="brand">SourceSharp <b>Debug API</b></span>

      {/* Only shown behind a proxy; a game serving this page directly has
          exactly one instance and nothing to choose between. */}
      <label className="instances" hidden={instances.instances === null}>
        instance
        <select
          value={instances.selected}
          onChange={event => onSelectInstance(event.target.value)}
        >
          {/* Nothing chosen is a state of its own — a picker showing the first
              game while talking to none of them is the wrong answer twice. */}
          <option value="" disabled hidden>choose a game</option>

          {(instances.instances ?? []).map(instance => (
            <option key={instance.pid} value={String(instance.pid)}>
              {instance.pid}{instance.map ? `  ${instance.map}` : ''}{instance.ok ? '' : '  (down)'}
            </option>
          ))}
        </select>
      </label>

      <span className="chips">
        {chips.map(([label, value]) => (
          <span className="chip" key={label}>{label} <b>{value}</b></span>
        ))}
      </span>

      <span className="spacer" />

      {/* Which build this game is running, on every tab. It answers a question
          nobody sets out to ask: every number on this page can be true of a game
          that was started before the change being looked for landed, and until
          this was here nothing on it could say so. */}
      <BuildStamp build={game?.build} />

      {/* Offered rather than taken: the page reloads itself on a rebuild, except
          when the map tab is holding edits that only exist here. */}
      <button className="rebuilt" hidden={!rebuilt} onClick={() => location.reload()}>
        the interface was rebuilt — reload
      </button>

      <span className="conn" data-state={status.connection satisfies Connection}>{status.detail}</span>
    </header>
  );
}

/**
 * The game this page is watching has gone.
 *
 * Loud on purpose. Everything on the page still reads perfectly — the log, the
 * entity list, the last status — and all of it is now a record of something that
 * stopped, which is the most misleading a debugger can be.
 */
export function StaleBanner({ shown, onPick }: {
  shown: boolean;
  onPick: () => void;
}): ReactNode {
  return (
    <div className="banner stale" role="alert" hidden={!shown}>
      <b>This game is no longer running.</b>{' '}
      Everything below is the last thing it said, not what is happening now.
      <button onClick={onPick}>Pick another</button>
    </div>
  );
}

/**
 * Which game, when the page cannot know.
 *
 * Asked rather than guessed: with several up, picking one silently would have
 * somebody reading the logs of a game they were not asking about — and every
 * command they then ran would go to it.
 */
export function InstancePicker({ instances, onChoose }: {
  instances: InstancesState;
  onChoose: (pid: string) => void;
}): ReactNode {
  if (!instances.mustChoose) return null;

  return (
    <div className="picker-backdrop">
      <div className="picker" role="dialog" aria-modal="true" aria-label="Choose a game">
        <h2>Which game?</h2>
        <p className="note">
          {instances.stale
            ? 'The game this page was watching has gone. These are the ones still up.'
            : 'Choose the game to watch. It is remembered in the address, so a reload '
              + 'comes back to it — and a socket left behind by a crash looks exactly '
              + 'like a game that is running, which is why nothing is picked for you.'}
        </p>

        <ul>
          {(instances.instances ?? []).map(instance => (
            <li key={instance.pid}>
              <button onClick={() => onChoose(String(instance.pid))}>
                <b>pid {instance.pid}</b>
                <span>{instance.map || 'no map'}{instance.ok ? '' : ' · not answering'}</span>
              </button>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}

export function Tabs({ active, onSelect }: {
  active: Tab;
  onSelect: (tab: Tab) => void;
}): ReactNode {
  return (
    <nav className="tabs">
      {TABS.map(tab => (
        <button
          key={tab}
          className="tab"
          aria-selected={tab === active}
          onClick={() => onSelect(tab)}
        >
          {TAB_LABELS[tab]}
        </button>
      ))}
    </nav>
  );
}

/**
 * The console, on every tab.
 *
 * Its suggestions are whole command lines rebuilt as the line is typed, because
 * a datalist matches against the whole value — completing the second word means
 * offering options that already contain the first.
 */
export function ConsoleBar({ onRun, onTyped, inputRef }: {
  onRun: (command: string) => void;
  onTyped: (typed: string) => void;
  inputRef?: RefObject<HTMLInputElement | null>;
}): ReactNode {
  const submit = (event: FormEvent<HTMLFormElement>): void => {
    event.preventDefault();

    const input = event.currentTarget.elements.namedItem('command') as HTMLInputElement;
    onRun(input.value);
    input.value = '';
  };

  return (
    <form className="console" onSubmit={submit}>
      <span className="prompt">{'>'}</span>
      <input
        ref={inputRef}
        id="console-input"
        name="command"
        list="sg-console"
        placeholder="console command, e.g. sv_cheats 1"
        autoComplete="off"
        spellCheck={false}
        onChange={event => onTyped(event.target.value)}
      />
      <button type="submit">Run</button>
    </form>
  );
}
