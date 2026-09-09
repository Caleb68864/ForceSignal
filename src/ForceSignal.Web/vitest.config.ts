import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    // Lib tests are pure and run in node, which is the fast default. A component test opts into
    // jsdom with a `@vitest-environment jsdom` docblock at the top of its file; the environment is
    // per file, so the DOM is only paid for where a render happens.
    environment: 'node',
    // Recent Node versions define their own experimental `localStorage`, and it is on globalThis
    // from the moment the process starts even though it does nothing without --localstorage-file:
    // reading it just gives back undefined. Vitest's jsdom environment will not overwrite a global
    // that is already there unless the name is on its own allow-list, and neither localStorage nor
    // sessionStorage is, so Node's inert one wins and jsdom's real Storage is never installed. The
    // two Dirtside screen tests were failing on `localStorage` being undefined rather than on
    // anything they set out to assert. Switching Node's off takes the name off globalThis entirely,
    // so jsdom's own Storage - which works, and is the one the app is written against - gets the
    // slot. Needs a Node that has the flag, which is any version new enough to have had the global
    // in the first place.
    execArgv: ['--no-experimental-webstorage'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
});
