import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    // Lib tests are pure and run in node, which is the fast default. A component test opts into
    // jsdom with a `@vitest-environment jsdom` docblock at the top of its file; the environment is
    // per file, so the DOM is only paid for where a render happens.
    environment: 'node',
    include: ['src/**/*.test.{ts,tsx}'],
  },
});
