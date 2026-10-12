import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    // jsdom rather than a real browser: these are the tests for logic — YAML,
    // graph layout, suggestions, the group store — and a real browser buys them
    // nothing. The things that genuinely need one (WebGL drawing, a drag that
    // hits a port, whether a hidden panel is hidden) stay in selftest.html,
    // which runs against the built bundle in headless Chrome.
    environment: 'jsdom',
    globals: true,
    include: ['src/**/*.test.ts', 'src/**/*.test.tsx'],
    setupFiles: ['src/test-setup.ts'],
  },
});
