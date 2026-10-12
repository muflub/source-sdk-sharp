/*
 * Filtering a listing by one flag.
 *
 * Flags were something to read off a row until the engine's own registry could
 * be listed. Among four thousand of them they are the most useful question the
 * page can ask: which of these are cheat-protected, which are replicated to
 * clients, which get written to config.cfg and will still be set tomorrow.
 *
 * One flag at a time, chosen from the flags actually present, rather than a row
 * of checkboxes for all thirty-two. Nearly every combination of two is empty,
 * and a filter that can be set to something no row can satisfy spends most of
 * its life showing nothing and looking broken.
 */

import type { ReactNode } from 'react';

/** What a row with no flags at all matches. Never a real name, which are TitleCase. */
export const NO_FLAGS = 'none';

/**
 * The flags on one row.
 *
 * "None" is what the game sends for a flagless convar and "" for a command this
 * mod registered, which is registered with no flags rather than with an empty
 * set of them. Both mean the same thing here: nothing to match.
 */
export function flagsOf(flags: string): string[] {
  return flags.split(/[,|]\s*/).map(flag => flag.trim()).filter(flag => flag !== '' && flag !== 'None');
}

/** Every flag present in a listing, sorted, for the picker to offer. */
export function flagsIn(rows: readonly { flags: string }[]): string[] {
  const present = new Set<string>();

  for (const row of rows) {
    for (const flag of flagsOf(row.flags)) present.add(flag);
  }

  return [...present].sort((a, b) => a.localeCompare(b));
}

/** Whether a row passes the filter. An unset filter passes everything. */
export function matchesFlag(flags: string, wanted: string): boolean {
  if (wanted === '') return true;

  const own = flagsOf(flags);

  return wanted === NO_FLAGS ? own.length === 0 : own.includes(wanted);
}

export function FlagFilter({ value, options, onChange }: {
  value: string;
  options: readonly string[];
  onChange: (value: string) => void;
}): ReactNode {
  return (
    <label>
      flag
      <select value={value} onChange={event => onChange(event.target.value)}>
        <option value="">any</option>
        {/* Worth offering explicitly: "which of these carry no flags at all" is
            a real question, and it is not answerable by picking one. */}
        <option value={NO_FLAGS}>(none)</option>
        {options.map(flag => <option key={flag} value={flag}>{flag}</option>)}
      </select>
    </label>
  );
}
