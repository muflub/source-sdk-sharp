/*
 * YAML in and out, in the browser.
 *
 * Done here rather than on the server for one reason: the server would need a
 * YAML library, and this assembly's whole design is to be absent unless someone
 * asked for it -- a dependency to serialise a debug view is the wrong trade. The
 * page already holds the entity model, so it is the natural place.
 *
 * Deliberately a subset. It reads what it writes, plus the usual things a person
 * would type when editing what it wrote: block maps, block sequences, quoted and
 * bare scalars, inline arrays, and comments. It does not do anchors, multiple
 * documents, or block scalars, and it says so rather than half-supporting them.
 */

import type { Connection, KeyValue } from '../types';

/** What this reads and writes. Everything a map file can hold, and no more. */
export type YamlValue = string | number | boolean | YamlValue[] | YamlMap;

export interface YamlMap { [key: string]: YamlValue }

/*
 * What an entity has to look like to be written out.
 *
 * Structural rather than the Entity type itself, because a group holds
 * something smaller than an entity and exports the same way. Anything that can
 * answer these questions can be written.
 */
export interface Writable {
  classname: string;
  targetname?: string;
  origin?: number[];
  angles?: number[];
  keyvalues?: KeyValue[];
  connections?: Connection[];
}

//---------------------------------------------------------------------------
// Writing.
//---------------------------------------------------------------------------

export function dump(value: unknown, indent = 0): string {
  const pad = '  '.repeat(indent);

  if (Array.isArray(value)) {
    if (value.length === 0) return pad + '[]\n';

    // Vectors and other short number lists stay on one line: an origin split
    // across three lines is harder to read than the thing it describes.
    if (value.every(v => typeof v === 'number')) {
      return pad + '[' + value.join(', ') + ']\n';
    }

    return value.map((item: unknown) => {
      const body = dump(item, indent + 1);
      return pad + '- ' + body.slice((indent + 1) * 2);
    }).join('');
  }

  if (value && typeof value === 'object') {
    const map = value as Record<string, unknown>;
    const keys = Object.keys(map);
    if (keys.length === 0) return pad + '{}\n';

    return keys.map(key => {
      const item = map[key];

      if (item && typeof item === 'object' && !(Array.isArray(item) && item.every(v => typeof v === 'number'))) {
        if (Array.isArray(item) && item.length === 0) return pad + quoteKey(key) + ': []\n';
        return pad + quoteKey(key) + ':\n' + dump(item, indent + 1);
      }

      return pad + quoteKey(key) + ': ' + scalar(item) + '\n';
    }).join('');
  }

  return pad + scalar(value) + '\n';
}

const KEY_SAFE = /^[A-Za-z_][A-Za-z0-9_.-]*$/;

const quoteKey = (key: string): string => (KEY_SAFE.test(key) ? key : JSON.stringify(key));

/*
 * A scalar, quoted whenever leaving it bare would change what it means.
 *
 * Map values are strings that constantly look like other types -- "1", "0.5",
 * "yes", "" -- and a round trip that turned "1" into a number would silently
 * rewrite the map. Anything that could be read as a non-string is quoted.
 */
function scalar(value: unknown): string {
  if (value === null || value === undefined) return '""';
  if (typeof value === 'number') return String(value);
  if (typeof value === 'boolean') return value ? 'true' : 'false';

  const text = String(value);

  if (text === '') return '""';

  if (Array.isArray(value)) return '[' + value.join(', ') + ']';

  const ambiguous = /^[-+]?[0-9.]+$/.test(text)
    || /^(true|false|yes|no|on|off|null|~)$/i.test(text)
    || /^[\s]|[\s]$|[:#\[\]{}",']|\n/.test(text);

  return ambiguous ? JSON.stringify(text) : text;
}

//---------------------------------------------------------------------------
// Reading.
//---------------------------------------------------------------------------

/**
 * Parses the subset above.
 *
 * Indentation-driven, like YAML itself. Lines are turned into a tree by
 * comparing indents, and only then interpreted -- which keeps the two kinds of
 * mistake (bad structure, bad value) apart and reportable.
 */
export function parse(text: string): YamlValue | null {
  const lines: Line[] = [];

  text.split(/\r?\n/).forEach((raw, number) => {
    const withoutComment = stripComment(raw);
    if (withoutComment.trim() === '') return;

    lines.push({
      indent: withoutComment.length - withoutComment.trimStart().length,
      text: withoutComment.trim(),
      number: number + 1,
    });
  });

  const [value] = parseBlock(lines, 0, lines[0]?.indent ?? 0);
  return value;
}

/** One significant line: what it says, how far in, and where it came from. */
interface Line { indent: number; text: string; number: number }

/** Drops a trailing comment, but not a # inside quotes -- values contain them. */
function stripComment(line: string): string {
  let quote: string | null = null;

  for (let i = 0; i < line.length; i++) {
    const c = line[i];

    if (quote) {
      if (c === '\\') i++;
      else if (c === quote) quote = null;
    } else if (c === '"' || c === "'") {
      quote = c;
    } else if (c === '#' && (i === 0 || /\s/.test(line[i - 1]!))) {
      return line.slice(0, i);
    }
  }

  return line;
}

function parseBlock(lines: Line[], at: number, indent: number): [YamlValue | null, number] {
  const line = lines[at];
  if (!line) return [null, at];

  return line.text.startsWith('- ') || line.text === '-'
    ? parseSequence(lines, at, indent)
    : parseMapping(lines, at, indent);
}

function parseSequence(lines: Line[], at: number, indent: number): [YamlValue[], number] {
  const result: YamlValue[] = [];

  while (lines[at]?.indent === indent && lines[at]!.text.startsWith('-')) {
    const rest = lines[at]!.text.slice(1).trim();
    at++;

    if (rest === '') {
      const [value, next] = parseBlock(lines, at, lines[at]?.indent ?? indent);
      result.push(value ?? '');
      at = next;
      continue;
    }

    // "- key: value" starts a mapping on the same line as the dash, which is
    // how anything readable is written and so has to be understood.
    if (isMappingEntry(rest)) {
      const inner: Line[] = [{ indent: indent + 2, text: rest, number: lines[at - 1]!.number }];

      while (lines[at] && lines[at]!.indent > indent) {
        inner.push(lines[at]!);
        at++;
      }

      const [value] = parseMapping(inner, 0, indent + 2);
      result.push(value);
      continue;
    }

    result.push(readScalar(rest));
  }

  return [result, at];
}

function parseMapping(lines: Line[], at: number, indent: number): [YamlMap, number] {
  const result: YamlMap = {};

  while (lines[at]?.indent === indent) {
    const line = lines[at]!;
    const split = splitKey(line.text);

    if (!split) throw new Error(`line ${line.number}: expected "key: value"`);

    const [key, rest] = split;
    at++;

    if (rest !== '') {
      result[key] = readScalar(rest);
      continue;
    }

    if (lines[at] && lines[at]!.indent > indent) {
      const [value, next] = parseBlock(lines, at, lines[at]!.indent);
      result[key] = value ?? '';
      at = next;
      continue;
    }

    // A key with nothing under it is an empty value, not an error: it is what
    // an empty map key round-trips to.
    result[key] = '';
  }

  return [result, at];
}

const isMappingEntry = (text: string): boolean => splitKey(text) !== null;

/** Splits "key: value" at the first colon that is not inside quotes. */
function splitKey(text: string): [string, string] | null {
  let quote: string | null = null;

  for (let i = 0; i < text.length; i++) {
    const c = text[i];

    if (quote) {
      if (c === '\\') i++;
      else if (c === quote) quote = null;
      continue;
    }

    if (c === '"' || c === "'") { quote = c; continue; }

    if (c === ':' && (i + 1 >= text.length || /\s/.test(text[i + 1]!))) {
      return [String(readScalar(text.slice(0, i).trim())), text.slice(i + 1).trim()];
    }
  }

  return null;
}

function readScalar(text: string): YamlValue {
  if (text === '' || text === '~' || text === 'null') return '';

  if (text.startsWith('[') && text.endsWith(']')) {
    const body = text.slice(1, -1).trim();
    if (body === '') return [];

    return splitFlow(body).map(readScalar);
  }

  if (text.startsWith('"') || text.startsWith("'")) {
    try {
      return text.startsWith('"') ? String(JSON.parse(text)) : text.slice(1, -1);
    } catch {
      return text.slice(1, -1);
    }
  }

  if (/^[-+]?\d+$/.test(text)) return parseInt(text, 10);
  if (/^[-+]?\d*\.\d+(e[-+]?\d+)?$/i.test(text)) return parseFloat(text);
  if (/^(true|false)$/i.test(text)) return text.toLowerCase() === 'true';

  return text;
}

function splitFlow(body: string): string[] {
  const parts: string[] = [];
  let depth = 0;
  let quote: string | null = null;
  let start = 0;

  for (let i = 0; i < body.length; i++) {
    const c = body[i];

    if (quote) {
      if (c === '\\') i++;
      else if (c === quote) quote = null;
    } else if (c === '"' || c === "'") {
      quote = c;
    } else if (c === '[' || c === '{') {
      depth++;
    } else if (c === ']' || c === '}') {
      depth--;
    } else if (c === ',' && depth === 0) {
      parts.push(body.slice(start, i).trim());
      start = i + 1;
    }
  }

  parts.push(body.slice(start).trim());
  return parts;
}

//---------------------------------------------------------------------------
// Entities, specifically.
//---------------------------------------------------------------------------

/** One entity in the shape this reads back. */
export function entityToObject(entity: Writable): YamlMap {
  const keyvalues: YamlMap = {};

  for (const kv of entity.keyvalues || []) {
    // The keys already spelled out above would appear twice otherwise, and an
    // edit to one copy and not the other has no defined meaning.
    if (['classname', 'targetname', 'origin', 'angles'].includes(kv.key.toLowerCase())) continue;
    keyvalues[kv.key] = kv.value;
  }

  const object: YamlMap = {
    classname: entity.classname,
    targetname: entity.targetname || '',
    origin: (entity.origin || [0, 0, 0]).map(n => Math.round(n * 100) / 100),
    angles: (entity.angles || [0, 0, 0]).map(n => Math.round(n * 100) / 100),
  };

  if (Object.keys(keyvalues).length) object.keyvalues = keyvalues;

  if ((entity.connections || []).length) {
    object.connections = entity.connections!.map(c => ({
      output: c.output,
      target: c.target,
      input: c.input,
      param: c.param || '',
      delay: c.delay || 0,
      times: c.times === undefined ? -1 : c.times,
    }));
  }

  return object;
}

export function toYaml(entities: Writable[], mapName: string, mode: string): string {
  const header =
    `# SourceSharp entity export\n` +
    `# map: ${mapName || '(none)'}\n` +
    `# source: ${mode}\n` +
    `# ${entities.length} entit${entities.length === 1 ? 'y' : 'ies'}\n\n`;

  return header + dump({
    map: mapName || '',
    source: mode,
    entities: entities.map(entityToObject),
  });
}

/** Reads back what toYaml wrote, and tolerates a bare list of entities too. */
export function fromYaml(text: string): YamlMap[] {
  const document = parse(text);

  if (Array.isArray(document)) return document as YamlMap[];

  const entities = (document as YamlMap | null)?.entities;
  if (Array.isArray(entities)) return entities as YamlMap[];

  throw new Error('expected a list of entities, or a document with an "entities:" key');
}
