/*
 * The map tab's options: which map, which mode, and everything that writes.
 *
 * Its own pane rather than a bar across the top. These are the things you set up
 * once and then work under — which map, static or live, whether to follow the
 * game — and they were taking a row away from the thing the tab is for.
 *
 * The editing buttons say what they are for by being disabled when they are not:
 * Save is off until something has changed, Save as is off with nothing to write,
 * and both are off in live mode, where the list is the engine's rather than the
 * map's.
 */

import { useState, type ReactNode } from 'react';

import type { MapInfo } from '../types';
import type { Mode } from './model';

export interface Prompt {
  label: string;
  value: string;
  kind?: string;
  action: string;
  onAnswer: (answer: string) => void;
}

export function MapOptions({
  info, map, mode, dirty, canSave, follow,
  onMapChange, onModeChange, onFollowChange,
  onReload, onLoadInGame, onReloadInGame,
  onAdd, onSave, onSaveAs, onRevert, onImport,
}: {
  info: MapInfo | null;
  map: string;
  mode: Mode;
  dirty: boolean;
  canSave: boolean;
  follow: boolean;
  onMapChange: (map: string) => void;
  onModeChange: (mode: Mode) => void;
  onFollowChange: (follow: boolean) => void;
  onReload: () => void;
  onLoadInGame: (map: string) => void;
  onReloadInGame: () => void;
  onAdd: () => void;
  onSave: () => void;
  onSaveAs: () => void;
  onRevert: () => void;
  onImport: (file: File) => void;
}): ReactNode {
  const editable = mode === 'static';

  return (
    <div className="map-options">
      <div className="toolbar">
      <label>
        map
        <select value={map} onChange={event => onMapChange(event.target.value)}>
          {(info?.available ?? []).map(name => (
            <option key={name} value={name}>
              {name}{name === info?.current ? '  (loaded)' : ''}
            </option>
          ))}
        </select>
      </label>

      <button
        title="load this map in the game — the level restarts"
        onClick={() => onLoadInGame(map)}
      >Load in game</button>
      <button
        title="restart the loaded map, which is how saved edits take effect"
        onClick={onReloadInGame}
      >Reload</button>

      <span className="modes">
        <label title="entities as the compiled map declares them">
          <input
            type="radio"
            name="map-mode"
            checked={mode === 'static'}
            onChange={() => onModeChange('static')}
          /> static
        </label>
        <label title="entities the engine holds right now">
          <input
            type="radio"
            name="map-mode"
            checked={mode === 'live'}
            onChange={() => onModeChange('live')}
          /> live
        </label>
      </span>

      <label>
        <input type="checkbox" checked={follow} onChange={event => onFollowChange(event.target.checked)} /> follow
      </label>

      <button title="read the map file again" onClick={onReload}>Re-read map</button>
      <button title="add a new entity to the map" disabled={!editable || !map} onClick={onAdd}>Add entity</button>
      <button
        title="write the edited entity list back over this map"
        disabled={!dirty || !editable}
        onClick={onSave}
      >{dirty ? 'Save map •' : 'Save map'}</button>
      <button
        title="write the edited entity list to a new map file"
        disabled={!canSave || !editable}
        onClick={onSaveAs}
      >Save as…</button>
      <button title="throw the edits away and read the map again" disabled={!dirty} onClick={onRevert}>Revert</button>
      <label className="file-button">
        Import YAML
        <input
          type="file"
          accept=".yaml,.yml,text/yaml"
          hidden
          onChange={event => {
            const file = event.target.files?.[0];
            if (file) onImport(file);
            event.target.value = '';
          }}
        />
      </label>

      </div>
    </div>
  );
}

/**
 * Asks for one value, inline.
 *
 * A window prompt cannot suggest anything, and every question this asks has a
 * good answer already in the page — an existing map name, a classname this mod
 * implements. So it is a bar rather than a dialog.
 */
export function PromptBar({ prompt, onCancel }: {
  prompt: Prompt | null;
  onCancel: () => void;
}): ReactNode {
  const [value, setValue] = useState('');

  if (!prompt) return <div className="wire-editor" hidden />;

  const answer = (): void => {
    const given = (value || prompt.value).trim();
    onCancel();
    if (given) prompt.onAnswer(given);
  };

  return (
    <div className="wire-editor">
      <span className="wire-from">{prompt.label}</span>
      <input
        key={prompt.label}
        autoFocus
        size={28}
        list={prompt.kind ? `sg-${prompt.kind}` : undefined}
        defaultValue={prompt.value}
        onChange={event => setValue(event.target.value)}
        onKeyDown={event => {
          if (event.key === 'Enter') { event.preventDefault(); answer(); }
          if (event.key === 'Escape') onCancel();
        }}
      />
      <button onClick={answer}>{prompt.action}</button>
      <button onClick={onCancel}>Cancel</button>
    </div>
  );
}
