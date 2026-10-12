/*
 * The schema: every C# type that networks state of its own, and what it costs.
 *
 * This is Part 13's first panel and it replaces reading prop names off
 * cs_sv_dump_nettable. Two things it says that a console dump does not:
 *
 *   - whether a type is actually ON THE WIRE. A state may be declared before its
 *     entity class has moved onto the hierarchy, and that one is described and
 *     networks nothing. "Declared but not sent" and "sent and decoding to zero"
 *     are the same picture from the outside, and only one of them is a bug.
 *
 *   - how close a layout is to outgrowing its tier. Growing past a tier boundary
 *     is the one schema change that still needs a restart, so it should be seen
 *     coming rather than discovered at connect.
 *
 * The tiers are REAL since Step 6, and everything tier-shaped here is a reading
 * of what native registered rather than a projection of the boundary rule. The
 * prop count in particular is what native built: the two shells put different
 * numbers of real props in front of their payloads, so there is no formula.
 *
 * The loudest thing on the pane is a type this build could not publish — a type
 * id collision, or a layout past the largest tier. Both are build-time facts
 * with no other symptom, and either means a class whose entities pair with the
 * wrong managed half or with none.
 *
 * WHAT IS NOT HERE, and where to find it. A CLIENT refusing an update because
 * its schema hash disagrees is the other half of Part 13's "schema mismatch must
 * be loud", and it cannot be shown here: the dev API is served by the server
 * module, and a client's refusals live in the client DLL's own memory with no
 * channel between them. It is loud in the console instead — a named warning per
 * type, and `cs_cl_schema_status` for the tally. See docs/divergences.md.
 */

import { Fragment, useMemo, useState, type ReactNode } from 'react';

import type { NetField, NetSchema, NetType, NetTypeDetail } from '../types';
import { fillLabel } from './hex';

export function SchemaPane({ schema, details, filter, onOpen }: {
  schema: NetSchema | null;

  /**
   * The field lists, by classname, for the rows that have been opened.
   *
   * The listing deliberately does not carry them: the fields are the bulk of the
   * schema and a page that polls would be sending every field of every type to
   * draw a table of counts. So a row asks for its own when it is opened.
   */
  details: Readonly<Record<string, NetTypeDetail>>;

  /** Shared with the inspector's toolbar; both panes filter on one box. */
  filter: string;

  /** A row was opened and wants its fields. */
  onOpen?: (className: string) => void;
}): ReactNode {
  const [open, setOpen] = useState<ReadonlySet<string>>(new Set());

  const text = filter.trim().toLowerCase();

  const types = useMemo(
    () => (schema?.types ?? []).filter(type =>
      text === ''
      || type.className.toLowerCase().includes(text)
      || type.typeName.toLowerCase().includes(text)),
    [schema, text]);

  if (schema === null) {
    return <p className="empty-pane">waiting for the game to describe its networked types…</p>;
  }

  if (schema.error !== undefined) {
    return <p className="banner error">{schema.error}</p>;
  }

  const toggle = (className: string): void => setOpen(previous => {
    const next = new Set(previous);

    if (next.has(className)) next.delete(className);
    else {
      next.add(className);
      onOpen?.(className);
    }

    return next;
  });

  const flagged = types.filter(type => type.tier.nearCeiling);
  const refused = schema.refused ?? [];

  return (
    <>
      {/* Above the near-ceiling warning, because this one is not "you will
          have a problem later" — it is a class that is not on the wire now. */}
      {refused.length > 0 && (
        <div className="banner error">
          {refused.length === 1 ? 'One type' : `${refused.length} types`} could not be
          published, so entities of {refused.length === 1 ? 'it' : 'them'} pair with the
          wrong managed class or with none. Either two classnames hash alike, or the
          layout is larger than the biggest tier.
          <ul>
            {refused.map(type => (
              <li key={type.className}>
                <b>{type.className}</b> — id {type.typeIdHex}, {type.layoutBytes} bytes
              </li>
            ))}
          </ul>
        </div>
      )}

      {/* The one thing on this pane that is a warning rather than a reading.
          Kept at the top because a type about to outgrow its tier is the only
          schema change here that costs a restart to fix. */}
      {flagged.length > 0 && (
        <div className="banner warn">
          {flagged.length === 1 ? 'One type is' : `${flagged.length} types are`} within a tenth
          of the tier {schema.tiersLive ? 'it is in' : 'it would be in'}. Outgrowing a tier is
          the one schema change that needs a restart.
          <ul>
            {flagged.map(type => (
              <li key={type.className}>
                <b>{type.className}</b> — {type.layoutBytes} of {type.tier.ceiling} bytes
              </li>
            ))}
          </ul>
        </div>
      )}

      <h2>Tiers</h2>
      <p className="note">
        {schema.tiersLive
          ? 'The registered payload tiers, read from the game rather than derived here. '
            + 'A tier with nothing on it is registered anyway, so a type can grow into it '
            + 'without a rebuilt client.'
          : 'No tiers to read. The game has not described them, which is not the same as '
            + 'a build without tiers.'}
      </p>

      <div className="cards">
        {schema.tiers.map(tier => (
          <div className="card tier-card" key={tier.name} data-live={tier.live}>
            <h3>{tier.name}</h3>
            <p className="sub">
              {tier.payloadBytes} byte payload · {tier.props} props
              {tier.live ? '' : ' · empty'}
            </p>
            <div className="kv">
              <span className="k">types</span>
              <span>{tier.count === 0 ? 'none' : tier.types.join(', ')}</span>
            </div>
          </div>
        ))}
      </div>

      {schema.oversized > 0 && (
        <p className="banner error">
          {schema.oversized} type(s) are too large for the biggest tier. Those cannot be
          networked by the payload design at all.
        </p>
      )}

      <h2>Types</h2>
      <p className="note">
        Every C# type that networks state of its own, with the table its two halves are
        paired by, its identity and its size. Click one to see its fields — this is what
        replaces reading prop names off <code>cs_sv_dump_nettable</code>.
      </p>

      <div className="table-wrap">
        <table className="schema-table">
          <thead>
            <tr>
              <th />
              <th>Class</th>
              <th>Table</th>
              <th>Shell</th>
              <th className="numeric">Fields</th>
              <th className="numeric">Props</th>
              <th className="numeric">Layout</th>
              <th className="numeric">Tier</th>
              <th>Type id</th>
              <th>Schema hash</th>
            </tr>
          </thead>
          <tbody>
            {types.map(type => (
              <TypeRow
                key={type.className}
                type={type}
                detail={details[type.className]}
                open={open.has(type.className)}
                onToggle={() => toggle(type.className)}
              />
            ))}
            {types.length === 0 && (
              <tr><td className="empty" colSpan={10}>no networked types match</td></tr>
            )}
          </tbody>
        </table>
      </div>
    </>
  );
}

function TypeRow({ type, detail, open, onToggle }: {
  type: NetType;
  detail: NetTypeDetail | undefined;
  open: boolean;
  onToggle: () => void;
}): ReactNode {
  return (
    <>
      <tr className={type.published ? undefined : 'invalid'}>
        <td><button className="expand" onClick={onToggle}>{open ? '▾' : '▸'}</button></td>
        <td className="name">
          {type.className}
          {/* Not a styling detail. A type nobody published networks nothing, and
              its fields decoding to zero on the client is the expected outcome
              rather than a decode bug. */}
          {!type.published && <span className="pill none">not on the wire</span>}
        </td>
        <td className="mono">{type.table}</td>
        <td className="mono">{type.shell}</td>
        <td className="mono numeric">{type.fieldCount}</td>
        <td className="mono numeric">
          {type.props}
          {/* The payload half, called out rather than folded in: it is the same
              number for every class on the hierarchy, so a row whose payload
              count differs from its neighbours' is the one thing worth seeing
              at a glance. */}
          {type.payloadWords > 0 && (
            <span className="dim"> ({type.payloadWords}p)</span>
          )}
        </td>
        <td className="mono numeric">{type.layoutBytes}b</td>
        <td className={`mono numeric${type.tier.nearCeiling ? ' near-ceiling' : ''}`}>
          {type.tier.ceiling > 0 ? `${type.tier.ceiling}b` : '—'}
          {' '}
          <span className="dim">{fillLabel(type.tier.ceiling, type.tier.fill)}</span>
        </td>
        <td className="mono">{type.typeIdHex}</td>
        <td className="mono">{type.schemaHashHex}</td>
      </tr>

      {open && (
        <tr className="detail">
          <td colSpan={10}>
            <p className="note">
              Declared by <b>{type.typeName}</b>. Native reserves {type.blobBytes} bytes
              for an instance of this class; the layout is {type.layoutBytes}.
            </p>
            {detail === undefined
              ? <p className="empty">reading this type's fields…</p>
              : <FieldTable fields={detail.fields ?? []} />}
          </td>
        </tr>
      )}
    </>
  );
}

/**
 * Where a field sits inside its storage, for the packing column.
 *
 * A packed field shares its dword, so `offset` names the word and not the field
 * -- four of func_breakable_surf's fields report offset 0 across four bytes
 * each. Without this column that reads exactly like the offset bug this panel
 * exists to catch, so the shift and the width are shown rather than left to be
 * inferred from two fields having the same number.
 */
function placement(field: NetField): string {
  if (!field.packed) return 'whole';

  const shift = field.bitShift ?? 0;
  return `bit ${shift}..${shift + field.bits - 1}`;
}

/**
 * A run of fields, either one whole field or one shared word and everything in
 * it.
 *
 * WHY THE TABLE IS BUILT OUT OF THESE and not out of rows. Before packing, a
 * field and a row were the same thing and every column could be a property of
 * the field. They are not the same thing any more: `bytes` belongs to the
 * STORAGE, and a storage can have four fields in it. Rendering one row per field
 * printed the word's four bytes four times, and a reader who added up the column
 * -- which is the obvious thing to do with a column headed Bytes -- got sixteen
 * bytes of colour inside an eight-byte class, and reasonably concluded the
 * packer had spent a word per channel.
 *
 * It had not. Grouping is what makes the two facts sit in different places: the
 * word's size is stated once, on the word, and each field states its own width
 * in bits and where in the word it is. There is no longer anywhere to read a
 * per-field byte cost off, because for a packed field there is no such number.
 */
interface Storage {
  /** The byte offset of the storage itself. */
  offset: number;

  /** How many bytes that storage is. Stated ONCE however many fields share it. */
  bytes: number;

  /** Whether the storage is shared, i.e. whether the fields below are packed. */
  packed: boolean;

  /**
   * The first field in it, held separately from the list so that the one row
   * every group definitely has needs no index and no undefined check.
   */
  head: NetField;

  /** The fields in it, in wire order, head included. One unless it is packed. */
  fields: NetField[];
}

/**
 * Fields grouped by the storage they occupy.
 *
 * Adjacent only, deliberately: the wire order is the placed order, so a packed
 * word's fields are always consecutive, and grouping non-adjacent rows would
 * reorder a table whose whole value is that it is in the order native built. If
 * two separated fields ever did claim one word that is a packer bug, and it
 * shows up here as two groups at the same offset -- which is exactly what
 * somebody should see.
 */
export function storages(fields: NetField[]): Storage[] {
  const groups: Storage[] = [];

  for (const field of fields) {
    const last = groups[groups.length - 1];

    if (last && field.packed && last.packed
        && last.offset === field.offset && last.bytes === field.bytes) {
      last.fields.push(field);
      continue;
    }

    groups.push({
      offset: field.offset, bytes: field.bytes,
      packed: field.packed === true, head: field, fields: [field],
    });
  }

  return groups;
}

/** How many of a shared word's bits any field actually claims. */
function claimed(group: Storage): number {
  return group.fields.reduce((total, field) => total + field.bits, 0);
}

/**
 * An enum field's members, spelled the way the value column will spell them.
 *
 * The kind column says `uint` for every enum, because that is honestly what
 * goes on the wire — so without this a reader has the type's NAME and no way to
 * turn a decoded `3` into a member. That is precisely the readability the
 * generic payload took away and these panels exist to give back.
 */
function EnumMembers({ field }: { field: NetField }): ReactNode {
  if (!field.enum) return null;

  return (
    <tr className="enum-members">
      <td />
      <td colSpan={6} className="mono">
        <span className="enum-kind">{field.enum.flags ? 'flags' : 'enum'}</span>
        {field.enum.members.map(member => (
          <span key={member.name} className="enum-member">
            {member.name} = {member.value}
          </span>
        ))}
      </td>
    </tr>
  );
}

/**
 * A type's fields, in wire order, grouped by the storage they occupy.
 *
 * THE COLUMNS, and what each one is answerable for:
 *
 *   Offset    where the STORAGE begins. Stated on the word, not on each field
 *             in it, because it is one number and repeating it four times is
 *             what made four fields look like four words.
 *   Name      the wire name, which is the string the two tables pair by.
 *   C# type   what the AUTHOR WROTE. Carried from the generator since it stopped
 *             being derivable: guessing it back from the kind and the byte count
 *             answered `uint` for every packed field, whatever it was declared.
 *   Kind      what it is ON THE WIRE, which is a different fact and is allowed
 *             to differ -- a `byte` is sent as an unsigned integer.
 *   Storage   how many bytes are read to reach it. A shared word states it ONCE;
 *             its fields say `shared`, because a packed field has no byte cost
 *             of its own and inventing one is the whole defect.
 *   Wire      the field's OWN width, in bits, for every row. This is the only
 *             cost column, and it is the one that adds up.
 *   At        whole, or which bits of the word.
 */
export function FieldTable({ fields }: { fields: NetField[] }): ReactNode {
  if (fields.length === 0) return <p className="empty">no fields loaded</p>;

  return (
    <table className="field-table">
      <thead>
        <tr>
          <th className="numeric">Offset</th>
          <th>Name</th>
          <th>C# type</th>
          <th>Kind</th>
          <th className="numeric">Storage</th>
          <th className="numeric">Wire</th>
          <th>At</th>
        </tr>
      </thead>
      <tbody>
        {storages(fields).map(group => group.packed ? (
          <Fragment key={`w${group.offset}:${group.head.name}`}>
            {/* The word itself. Its size is here and nowhere else, so the
                column below it cannot be added up into a wrong total. */}
            <tr className="packed-word">
              <td className="mono numeric">{group.offset}</td>
              <td colSpan={6} className="mono">
                <span className="word-tag">shared word</span>
                {group.bytes} bytes holding {group.fields.length}
                {group.fields.length === 1 ? ' field' : ' fields'}
                {', '}{claimed(group)} of {group.bytes * 8} bits used
              </td>
            </tr>
            {group.fields.map(field => (
              <Fragment key={field.name}>
                <tr className="packed-member">
                  <td />
                  <td className="name">{field.name}</td>
                  <td className="mono">{field.csType}</td>
                  <td className="mono">{field.kind}</td>
                  <td className="mono numeric shared">shared</td>
                  <td className="mono numeric">{field.bits} b</td>
                  <td className="mono">{placement(field)}</td>
                </tr>
                <EnumMembers field={field} />
              </Fragment>
            ))}
          </Fragment>
        ) : (
          <Fragment key={group.head.name}>
            <tr>
              <td className="mono numeric">{group.offset}</td>
              <td className="name">{group.head.name}</td>
              <td className="mono">{group.head.csType}</td>
              <td className="mono">{group.head.kind}</td>
              <td className="mono numeric">{group.bytes} B</td>
              <td className="mono numeric">{group.head.bits} b</td>
              <td className="mono">{placement(group.head)}</td>
            </tr>
            <EnumMembers field={group.head} />
          </Fragment>
        ))}
      </tbody>
    </table>
  );
}
