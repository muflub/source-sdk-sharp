/*
 * The interface.
 *
 * The shell: what tab is showing, what pane within it, which game is being
 * addressed, whether it is answering, and the one poll loop everything is
 * driven from. Each tab owns its own contents; what is here is what belongs to
 * none of them.
 */

import { useCallback, useEffect, useRef, useState } from 'react';

import { runCommand } from './api';
import { ConsoleBar, Header, InstancePicker, StaleBanner, Tabs, TABS, type Tab } from './components/Chrome';
import { useInstances } from './hooks/useInstances';
import { CommandsTab } from './tabs/Commands';
import { LogsTab, type LogsHandle } from './tabs/Logs';
import { ScriptedTab } from './tabs/Scripted';
import type { EventsHandle } from './map/EventsPane';
import { MapTab, resetMap, type MapHandle } from './map/MapTab';
import { MapFileTab } from './mapfile/MapFileTab';
import { MockupsTab } from './mockups/MockupsTab';
import { NetTablesTab } from './nettable/NetTablesTab';
import { PredictionTab } from './prediction/PredictionTab';
import { createSelection } from './map/selection';
import { ConVarsTab } from './tabs/ConVars';
import { ServicesTab } from './tabs/Services';
import { useLiveReload } from './hooks/useLiveReload';
import { usePoll } from './hooks/usePoll';
import { useStatus } from './hooks/useStatus';
import * as mapStore from './map/store';

import * as suggest from './suggest';

/** The tab named in the address, so a reload comes back to the same view. */
function tabFromLocation(): Tab {
  const name = location.hash.replace(/^#/, '').split('/')[0] ?? '';
  return (TABS as readonly string[]).includes(name) ? name as Tab : 'logs';
}

/** An entity named after the tab, so a link can point at one. */
function selectionFromLocation(): string {
  const parts = location.hash.replace(/^#/, '').split('/');
  return parts.length > 1 ? decodeURIComponent(parts.slice(1).join('/')) : '';
}

export function App() {
  const [tab, setTab] = useState<Tab>(tabFromLocation);
  const [status, pollStatus] = useStatus();
  const consoleRef = useRef<HTMLInputElement>(null);
  const logsRef = useRef<LogsHandle | null>(null);
  const eventsRef = useRef<EventsHandle | null>(null);
  const mapRef = useRef<MapHandle | null>(null);

  /*
   * Which entities are collected, as opposed to which one is being looked at.
   * The entity list marks them and the groups pane captures them, so it belongs
   * to neither and lives here.
   */
  const [selection] = useState(createSelection);

  const [instances, pollInstances, selectInstance] = useInstances(
    useCallback(() => {
      logsRef.current?.reset();
      eventsRef.current?.reset();
      resetMap();
    }, []));

  const show = useCallback((next: Tab) => {
    setTab(next);

    // The entity after the slash belongs to the map tab; rewriting the hash on
    // every tab change would throw it away.
    if (tabFromLocation() !== next) location.hash = next;
  }, []);

  useEffect(() => {
    const onHashChange = (): void => show(tabFromLocation());
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, [show]);

  /**
   * Runs a console command and points the log at what it produced.
   *
   * The reply says where in the log its own output starts; marking from there is
   * the difference between "the command ran" and "here is what it did", which on
   * a busy server are not the same question.
   */
  const run = useCallback(async (command: string): Promise<void> => {
    if (!command.trim()) return;

    let result;

    try {
      result = await runCommand(command);
    } catch {
      // Into the log the user is already looking at rather than an alert: a
      // command that did not reach the game is a line about this page.
      logsRef.current?.pushLocal('debug-api: could not reach the game');
      return;
    }

    if (result.error) {
      logsRef.current?.pushLocal(`debug-api: ${result.error}`);
      return;
    }

    if (result.logFrom !== undefined) {
      show('logs');
      logsRef.current?.markFrom(result.logFrom);
    }
  }, [show]);

  /*
   * A rebuilt interface reloads the page, unless the map tab is holding edits
   * that exist nowhere else yet — in which case it is offered in the header.
   * Only a debug build serving this page off disk can rebuild under itself.
   */
  const [rebuilt, checkForRebuild] = useLiveReload(() => !mapStore.state.dirty);

  usePoll(async counter => {
    /*
     * Which game, before anything is asked of one.
     *
     * Behind a proxy with several games up there is nothing to guess from: the
     * proxy refuses a request that names none, so every poll before the page has
     * chosen comes back as an error. Cheap enough at this rate to be first.
     */
    if (counter % 40 === 0) await pollInstances();

    await logsRef.current?.poll();
    if (counter % 4 === 0 && tab === 'map') await mapRef.current?.poll();

    // The event feed only samples while something is reading it, so polling it
    // anywhere but on its own tab would keep the game sampling for nothing.
    if (counter % 4 === 0 && tab === 'map') await eventsRef.current?.poll();

    // Status and the instance list change far more slowly than a log does, so
    // neither runs at the log's rate.
    if (counter % 4 === 0) await pollStatus();

    // Twice the status rate: a rebuild is a thing you are waiting for, and two
    // seconds of staring at an unchanged page is long enough to press F5.
    if (counter % 8 === 0) await checkForRebuild();
  });

  return (
    <>
      <StaleBanner shown={instances.stale} onPick={() => void pollInstances()} />

      <Header
        status={status}
        instances={instances}
        rebuilt={rebuilt}
        onSelectInstance={selectInstance}
      />

      <InstancePicker instances={instances} onChoose={selectInstance} />
      <Tabs active={tab} onSelect={show} />

      <main>

              <section className="panel" data-panel="logs" hidden={tab !== 'logs'}>
        <LogsTab visible={tab === 'logs'} handle={logsRef} />
      </section>

              <section className="panel" data-panel="commands" hidden={tab !== 'commands'}>
        <CommandsTab
          visible={tab === 'commands'}
          onRun={command => { void run(command); }}
          onPrime={command => {
            // Straight into the console at the bottom, focused and ready for
            // arguments — which is the whole difference from Run.
            const input = consoleRef.current;
            if (!input) return;

            input.value = command;
            input.focus();
            suggest.refreshConsole(command);
          }}
          onLoaded={commands => suggest.refreshFromListings({ commands })}
        />
      </section>

              <section className="panel" data-panel="convars" hidden={tab !== 'convars'}>
        <ConVarsTab
          visible={tab === 'convars'}
          onLoaded={convars => suggest.refreshFromListings({ convars })}
        />
      </section>

              {/* What an entity may ask for. Beside the two console listings
                  because it holds their names too, under the other key. */}
              <section className="panel" data-panel="services" hidden={tab !== 'services'}>
        <ServicesTab visible={tab === 'services'} />
      </section>

              {/* The entity debugger. Two sources of truth, five ways to look at them. */}
              <section className="panel map-panel" data-panel="map" hidden={tab !== 'map'}>
        <MapTab
          visible={tab === 'map'}
          select={selectionFromLocation()}
          selection={selection}
          handle={mapRef}
          eventsHandle={eventsRef}
          onResult={result => {
            if (result.error) logsRef.current?.pushLocal(`debug-api: ${result.error}`);
            else if (result.logFrom !== undefined) logsRef.current?.markFrom(result.logFrom);
          }}
        />
      </section>

              {/* The file rather than the entities in it. Its own tab because
                  "what does this map do" and "what is in this file" send you
                  looking in different directions. */}
              <section className="panel mapfile-panel" data-panel="mapfile" hidden={tab !== 'mapfile'}>
        <MapFileTab visible={tab === 'mapfile'} />
      </section>

              <section className="panel" data-panel="scripted" hidden={tab !== 'scripted'}>
        <ScriptedTab
          visible={tab === 'scripted'}
          onLoaded={entityClasses => suggest.refreshFromListings({ entityClasses })}
        />
      </section>

                    {/* The wire: what each C# type networks, and what one entity's
                  bytes actually say. Separate from the entity debugger because
                  "what does this map declare" and "what is on the wire" send you
                  looking in different directions. */}
              <section className="panel" data-panel="nettables" hidden={tab !== 'nettables'}>
        <NetTablesTab visible={tab === 'nettables'} />
      </section>

              {/* Whether the client's guess at the next frame matched what the
                  server said. Its own tab rather than a pane of the wire above,
                  because "what is on the wire" and "how far apart did the two
                  sides end up, and is that gap growing" are different
                  questions with different answers. */}
              <section className="panel" data-panel="prediction" hidden={tab !== 'prediction'}>
        <PredictionTab visible={tab === 'prediction'} />
      </section>

              {/* The game's UI without the game: make ui-preview's pictures. */}
              <section className="panel" data-panel="mockups" hidden={tab !== 'mockups'}>
        <MockupsTab visible={tab === 'mockups'} />
      </section>

            </main>

      <ConsoleBar
        inputRef={consoleRef}
        onRun={command => { void run(command); }}
        onTyped={typed => suggest.refreshConsole(typed)}
      />
    </>
  );
}
