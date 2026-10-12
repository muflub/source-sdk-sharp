/*
 * One entity's payload, three ways at once.
 *
 * Part 13's second panel, and the one that makes an offset bug obvious rather
 * than deducible. The three views are not three ways of saying the same thing:
 *
 *   - the HEX GRID is the bytes as they are, with the field boundaries drawn
 *     onto it. It is the view that is still true when the decode is what is
 *     under suspicion.
 *   - the DECODED TABLE is those bytes read through the layout. Agreement
 *     between it and the grid is the thing being checked.
 *   - the TINTING is what changed since the last sample, and it is what turns a
 *     wrong offset into a visible event: a field reading the wrong bytes moves
 *     when its NEIGHBOUR is written, and nothing else about it looks wrong.
 *
 * A first sample is drawn as a first sample and not as "nothing changed". Those
 * are different facts and only one of them says the value is not moving.
 */

import { type ReactNode } from 'react';

import type { NetPayload } from '../types';
import { fieldColour, hexCells, hexRows, offsetLabel } from './hex';

export function InspectorPane({ payload, index, live, onIndex, onLive, onRefresh }: {
  payload: NetPayload | null;
  index: string;
  live: boolean;
  onIndex: (index: string) => void;
  onLive: (live: boolean) => void;
  onRefresh: () => void;
}): ReactNode {
  return (
    <>
      <div className="toolbar">
        <label>
          entity index
          <input
            type="number"
            min={0}
            className="entity-index"
            value={index}
            onChange={event => onIndex(event.target.value)}
          />
        </label>
        <label>
          <input type="checkbox" checked={live} onChange={event => onLive(event.target.checked)} /> live
        </label>
        <button onClick={onRefresh}>Sample</button>
        {payload !== null && payload.error === undefined && (
          <span className="count">
            {payload.classname} · handle {payload.handle} · {payload.blob.bytes} bytes
          </span>
        )}
      </div>

      <PayloadBody payload={payload} />
    </>
  );
}

function PayloadBody({ payload }: { payload: NetPayload | null }): ReactNode {
  if (payload === null) {
    return <p className="empty-pane">pick an entity index and sample it.</p>;
  }

  if (payload.error !== undefined) {
    return <p className="banner error">{payload.error}</p>;
  }

  const cells = hexCells(payload.blob.hex, payload.fields, payload.changed.runs);
  const rows = hexRows(cells, payload.blob.rowBytes);

  return (
    <>
      {/* A class that networks nothing of its own is not an error and is the
          common case — most of the hierarchy shares one table. Decoding its
          bytes against somebody else's layout would be far worse than saying
          there is nothing to decode them with. */}
      {payload.type === null && (
        <p className="banner warn">
          <b>{payload.classname}</b> networks no state of its own, so there is no layout to
          read these bytes through. The hex below is the shared blob native reserves for
          every entity on the hierarchy.
        </p>
      )}

      {/* Always wrong, whatever else is going on: the props addressing this
          entity would be reading past the end of it. */}
      {payload.blobShorterThanLayout === true && (
        <p className="banner error">
          The blob is {payload.blob.bytes} bytes and the layout needs {payload.layoutBytes}.
          Every field past the end is unreadable, and the props addressing them point past
          the entity.
        </p>
      )}

      <AgreementLine payload={payload} />

      <ChangeLine payload={payload} />

      <h2>Bytes</h2>
      <p className="note">
        The blob as it is, with each field's own bytes bracketed and named. Bytes that
        moved since the previous sample are tinted.
      </p>

      <div className="hex-grid">
        {rows.map(row => (
          <div className="hex-row" key={row[0]?.offset ?? 0}>
            <span className="hex-offset">{offsetLabel(row[0]?.offset ?? 0)}</span>
            <span className="hex-bytes">
              {row.map(cell => (
                <span
                  key={cell.offset}
                  className="hex-byte"
                  data-colour={fieldColour(payload.fields, cell.field)}
                  data-start={cell.fieldStart}
                  data-end={cell.fieldEnd}
                  data-changed={cell.changed}
                  data-unclaimed={cell.field === null}
                  title={cell.field === null
                    ? `${offsetLabel(cell.offset)} — no field claims this byte`
                    : `${offsetLabel(cell.offset)} — ${cell.field.name} (${cell.field.csType})`}
                >
                  {cell.hex}
                </span>
              ))}
            </span>
            <span className="hex-ascii">
              {row.map(cell => (
                <span key={cell.offset} data-changed={cell.changed}>{cell.ascii}</span>
              ))}
            </span>
          </div>
        ))}
      </div>

      <h2>Decoded</h2>
      <p className="note">
        The same bytes read through <b>{payload.type?.className ?? 'no layout'}</b>. A value
        here that disagrees with the bytes above is the failure these routes exist to catch.
      </p>

      <div className="table-wrap">
        <table className="field-table">
          <thead>
            <tr>
              <th className="numeric">Offset</th>
              <th>Name</th>
              <th>C# type</th>
              <th>Value</th>
              <th>Bytes</th>
            </tr>
          </thead>
          <tbody>
            {payload.fields.map((field, i) => (
              <tr
                key={field.name}
                className={field.changed ? 'changed' : undefined}
                data-colour={i % 6}
              >
                <td className="mono numeric">{field.offset}</td>
                <td className="name">{field.name}</td>
                <td className="mono">{field.csType}</td>
                <td className="mono value">
                  {field.present
                    ? field.value
                    // No value is invented for a field past the end of the blob:
                    // a fabricated zero is a lie the reader would act on.
                    : <span className="dim">past the end of the blob</span>}
                </td>
                <td className="mono">{field.hex}</td>
              </tr>
            ))}
            {payload.fields.length === 0 && (
              <tr><td className="empty" colSpan={5}>nothing to decode these bytes with</td></tr>
            )}
          </tbody>
        </table>
      </div>
    </>
  );
}

/**
 * Whether the typed props and the generic payload agree, while both carry this
 * entity (plan_nettable_generic.md Part 14 Step 3).
 *
 * Only two of the four answers are worth a line. An entity with no payload is
 * nearly every entity in the map, and a build that cannot answer is an old
 * build — painting either of those would make the one answer that matters
 * invisible among them.
 */
function AgreementLine({ payload }: { payload: NetPayload }): ReactNode {
  const agreement = payload.payload;

  if (agreement === undefined || !agreement.live || agreement.state === 'none')
    return null;

  if (agreement.state === 'agrees') {
    return (
      <p className="note" data-state="agrees">
        The typed props and the generic payload agree on all {agreement.words} word(s).
      </p>
    );
  }

  return (
    <p className="banner error">
      The generic payload disagrees with the typed props from word{' '}
      {agreement.firstBadWord} of {agreement.words}. One of the two prop sets is reading
      the wrong bytes, and the payload is the new one.
    </p>
  );
}

/**
 * What moved, and how long ago.
 *
 * A first sample says so. It is not the same fact as "nothing changed", and a
 * line that rendered the two alike would let a still entity and a newly watched
 * one look identical — which is exactly the confusion the diff exists to remove.
 */
function ChangeLine({ payload }: { payload: NetPayload }): ReactNode {
  const { changed } = payload;

  if (changed.first) {
    return (
      <p className="note change-line" data-state="first">
        First sample of this entity. Sample it again to see what moves.
      </p>
    );
  }

  const moved = payload.fields.filter(field => field.changed).map(field => field.name);

  return (
    <p className="note change-line" data-state={changed.bytes > 0 ? 'moved' : 'still'}>
      {changed.bytes === 0
        ? `Nothing changed in the last ${changed.sinceSeconds.toFixed(1)}s.`
        : `${changed.bytes} byte(s) changed in the last ${changed.sinceSeconds.toFixed(1)}s: `
          // Bytes moving that no field claims is worth saying out loud rather
          // than reporting as an empty list. It means something is writing into
          // padding, or into the tail native reserves for a greedier class, and
          // either way the layout does not describe what is happening.
          + (moved.length > 0 ? moved.join(', ') : 'in bytes no field claims')}
      {changed.resized && ' The blob changed length, so this diff is partial.'}
    </p>
  );
}
