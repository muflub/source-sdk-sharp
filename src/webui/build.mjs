/*
 * Builds the web interface into src/debug_api/ui (Debug.Api.Server embeds it).
 *
 * That directory is what the assembly embeds and what the game serves, so it is
 * output rather than source — everything under src/ here is the source now.
 *
 * One bundle, with React inside it. The page is served by the game over a
 * loopback socket with no network behind it, so anything left to be fetched from
 * elsewhere would simply never load. That constraint is why there is no CDN link
 * and why the bundle is checked for size: it has to travel down a socket the
 * game is answering on its own listener thread.
 */

import * as esbuild from 'esbuild';
import { cp, mkdir, readdir, readFile, rm, stat, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const out = join(here, '..', 'debug_api', 'ui');

const watch = process.argv.includes('--watch');
const dev = watch || process.argv.includes('--dev');

/** Files served as they are: the page itself, and anything not built. */
const copied = ['index.html', 'selftest.html'];

const options = {
  // Two entry points: the page, and the self-test that needs a real browser.
  entryPoints: {
    app: join(here, 'src', 'main.tsx'),
    selftest: join(here, 'src', 'selftest.tsx'),
  },
  bundle: true,
  format: 'esm',
  target: ['chrome120', 'firefox120', 'safari17'],
  outdir: out,
  assetNames: '[name]',

  // Readable in a debug build, because the first thing anyone does with a broken
  // page is open the console and look at the stack.
  //
  // The map is a separate file rather than inlined: inlining tripled the bundle,
  // and the bundle is embedded in an assembly the game loads. The .map is left
  // out of that embed and read from disk, which is where a debug build looks
  // first anyway.
  minify: !dev,
  sourcemap: dev ? 'linked' : false,
  legalComments: 'none',

  define: { 'process.env.NODE_ENV': dev ? '"development"' : '"production"' },
  logLevel: 'info',
};

async function copyStatic(dir) {
  for (const name of copied) {
    await cp(join(here, name), join(dir, name)).catch(() => {});
  }
}

/**
 * Makes `out` hold exactly what `staged` holds, writing only the files whose
 * bytes differ and deleting the ones no longer produced. Debug.Api.Server
 * embeds `out`, and MSBuild decides by timestamp: a build that rewrote every
 * file, as rm-and-rebuild did, recompiled the assembly (and what depends on
 * it) on every `make test`, though nothing in it had changed.
 */
async function sync(staged, dir) {
  await mkdir(dir, { recursive: true });
  const want = new Set(await readdir(staged));
  let wrote = 0;
  for (const name of want) {
    const next = await readFile(join(staged, name));
    const prev = await readFile(join(dir, name)).catch(() => null);
    if (prev === null || !prev.equals(next)) {
      await writeFile(join(dir, name), next);
      wrote++;
    }
  }
  for (const name of await readdir(dir)) {
    if (!want.has(name)) {
      await rm(join(dir, name), { recursive: true, force: true });
      wrote++;
    }
  }
  return wrote;
}

if (watch) {
  await rm(out, { recursive: true, force: true });
  await mkdir(out, { recursive: true });
  const context = await esbuild.context(options);
  await context.watch();
  await copyStatic(out);
  console.log(`watching; output goes to ${out}`);
} else {
  const staged = `${out}.staging`;
  await rm(staged, { recursive: true, force: true });
  await mkdir(staged, { recursive: true });
  await esbuild.build({ ...options, outdir: staged });
  await copyStatic(staged);
  const wrote = await sync(staged, out);
  await rm(staged, { recursive: true, force: true });
  console.log(`${wrote} file(s) changed in ${out}`);

  const bundle = await stat(join(out, 'app.js'));
  const styles = await stat(join(out, 'app.css')).catch(() => ({ size: 0 }));

  console.log(`app.js ${(bundle.size / 1024).toFixed(0)}kB, app.css ${(styles.size / 1024).toFixed(0)}kB`);
}
