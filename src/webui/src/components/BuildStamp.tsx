/*
 * WHICH BUILD THE GAME BEING WATCHED IS RUNNING.
 *
 * In the bar, on every tab, because the question it answers is never the one
 * somebody set out to ask. It came from an hour spent on a pane that was
 * correctly reporting a game started before the change landed — every number on
 * screen was true, and none of them could say which build produced them.
 *
 * So: a line in the chrome that is always there, and a panel behind it for the
 * detail. Not a tab. Nobody would visit a tab called "Build" before they were
 * already confused, and by then the thing they are looking at has misled them.
 */

import { type ReactNode } from 'react';

import { advice, clock, dirtiness, full, outOfDate, short, sourceDiverged, type Which } from './build';
import { useUrlFlag } from '../hooks/useUrlState';
import type { Build, BuildHalf } from '../types';

export function BuildStamp({ build }: { build: Build | undefined }): ReactNode {
  /*
   * Open-ness in the address, like every other "what am I looking at" on this
   * page. "Your game is from the wrong build, look —" is a link, and a link that
   * arrives with the panel shut has not said the thing it was sent to say.
   */
  const [open, setOpen] = useUrlFlag('build');

  // A game too old to carry a stamp. Nothing at all rather than a placeholder:
  // this is a strip somebody reads at a glance, and an empty reading in it is
  // noise.
  if (!build) return null;

  const stale = outOfDate(build);
  const diverged = sourceDiverged(build);

  return (
    <span className="build" data-stale={stale ? 'yes' : 'no'}>
      <button
        className="build-line"
        aria-expanded={open}
        title="Which build this game is running. Click for the detail."
        onClick={() => setOpen(!open)}
      >
        <b>{build.managed.branch || build.native.branch || 'build'}</b>
        <span className="half">nat {short(build.native)}</span>
        <span className="half">man {short(build.managed)}</span>
        {stale && <span className="mark">rebuilt</span>}
        {diverged && <span className="mark">two trees</span>}
      </button>

      {open && <BuildPane build={build} />}
    </span>
  );
}

/**
 * The detail, as a panel under the bar rather than a tab of its own.
 *
 * Both halves side by side, because the whole reason there are two of them is
 * that they can disagree, and a view showing one at a time cannot show a
 * disagreement.
 */
function BuildPane({ build }: { build: Build }): ReactNode {
  return (
    <div className="build-pane" role="dialog" aria-label="Build">
      <div className="build-halves">
        <BuildHalfView which="native" half={build.native} />
        <BuildHalfView which="managed" half={build.managed} />
      </div>

      <p className="note">
        Two stamps, because there are two halves. <code>cs_sv_reload</code> replaces the
        managed one in a running process and leaves native exactly as it was, so
        the times differing is the normal reading and not a fault. The hash is
        over the bytes that were <b>loaded</b> — not over a commit, which would be
        wrong about every build made from a tree with uncommitted work in it.
      </p>

      {sourceDiverged(build) && (
        <p className="warn">
          The two halves were built from different source. The managed half has been
          reloaded from a tree the running native half was not built from.
        </p>
      )}
    </div>
  );
}

function BuildHalfView({ which, half }: { which: Which; half: BuildHalf }): ReactNode {
  const say = advice(which, half.freshness);

  return (
    <div className="build-half" data-fresh={half.freshness}>
      <h3>{which}</h3>

      {!half.known ? (
        <p className="note">Nothing stamped this half.</p>
      ) : (
        <dl>
          <dt>built</dt>
          <dd title={full(half.at)}>{clock(half.at)}</dd>

          <dt>hash</dt>
          <dd className="mono">{half.hash}</dd>

          <dt>branch</dt>
          <dd className="mono">{half.branch || '(detached)'}</dd>

          <dt>commit</dt>
          <dd className="mono">{half.commit || 'unknown'}</dd>

          <dt>tree</dt>
          <dd>{dirtiness(half)}</dd>

          <dt>files</dt>
          <dd>{half.parts}</dd>

          <dt>from</dt>
          <dd className="mono path">{half.path}</dd>
        </dl>
      )}

      {say && <p className="warn">{say}</p>}
    </div>
  );
}
