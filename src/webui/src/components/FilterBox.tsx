/*
 * A filter box that suggests from what is on the page.
 *
 * A native datalist was doing this, and its two habits made it wrong here: it
 * opens on focus with the whole list — hundreds of names, before a single
 * character says which of them is wanted — and the browser puts it where it
 * likes. So this draws its own, under the box, and only once enough has been
 * typed to be worth answering.
 */

import { useEffect, useRef, useState, type ReactNode } from 'react';

/** Below this, a filter matches so much that a list of matches says nothing. */
const MIN_CHARS = 3;

/** Enough to choose from without covering what is being filtered. */
const MOST = 8;

export function FilterBox({ value, options, placeholder, onChange }: {
  value: string;
  options: readonly string[];
  placeholder: string;
  onChange: (value: string) => void;
}): ReactNode {
  const [open, setOpen] = useState(false);
  const [highlight, setHighlight] = useState(0);
  const host = useRef<HTMLDivElement>(null);

  const text = value.trim().toLowerCase();

  const matches = text.length < MIN_CHARS ? [] : rank(options, text).slice(0, MOST);

  // An exact match is a filter that has arrived, not one being typed; leaving
  // the list up would cover the rows it just found.
  const shown = open && matches.length > 0 && !(matches.length === 1 && matches[0]!.toLowerCase() === text)
    ? matches
    : [];

  useEffect(() => setHighlight(0), [value]);

  // A click anywhere else means the list has been left rather than used.
  useEffect(() => {
    if (shown.length === 0) return;

    const onDown = (event: MouseEvent): void => {
      if (!host.current?.contains(event.target as Node)) setOpen(false);
    };

    document.addEventListener('mousedown', onDown);
    return () => document.removeEventListener('mousedown', onDown);
  }, [shown.length]);

  const choose = (name: string): void => {
    onChange(name);
    setOpen(false);
  };

  return (
    <div className="filter-box" ref={host}>
      <input
        type="search"
        placeholder={placeholder}
        value={value}
        autoComplete="off"
        onChange={event => { onChange(event.target.value); setOpen(true); }}
        onFocus={() => setOpen(true)}
        onKeyDown={event => {
          if (event.key === 'Escape') { setOpen(false); return; }
          if (shown.length === 0) return;

          if (event.key === 'ArrowDown') {
            event.preventDefault();
            setHighlight(at => (at + 1) % shown.length);
          } else if (event.key === 'ArrowUp') {
            event.preventDefault();
            setHighlight(at => (at - 1 + shown.length) % shown.length);
          } else if (event.key === 'Enter') {
            event.preventDefault();
            choose(shown[highlight] ?? shown[0]!);
          }
        }}
      />

      {shown.length > 0 && (
        <ul className="suggestions" role="listbox">
          {shown.map((name, index) => (
            <li key={name}>
              <button
                role="option"
                aria-selected={index === highlight}
                className={index === highlight ? 'highlighted' : ''}
                onMouseEnter={() => setHighlight(index)}

                // Mousedown rather than click: the input's blur would otherwise
                // close the list before the click landed on anything.
                onMouseDown={event => { event.preventDefault(); choose(name); }}
              >{name}</button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/** Names containing the text, with the ones that start with it first. */
function rank(options: readonly string[], text: string): string[] {
  const starts: string[] = [];
  const contains: string[] = [];

  for (const option of options) {
    const lower = option.toLowerCase();

    if (lower.startsWith(text)) starts.push(option);
    else if (lower.includes(text)) contains.push(option);
  }

  return [...starts, ...contains];
}
