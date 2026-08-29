import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    // Every test here is a pure lib test. A DOM environment is a dependency this suite does not
    // yet need; add jsdom with the first component test.
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
});
