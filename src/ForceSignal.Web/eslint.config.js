import js from '@eslint/js';
import reactHooks from 'eslint-plugin-react-hooks';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  { ignores: ['dist', 'node_modules'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ['**/*.{ts,tsx}'],
    plugins: { 'react-hooks': reactHooks },
    rules: {
      // Only the two rules about hooks themselves. The plugin's recommended set also carries the
      // React Compiler readiness rules, which reject the ref-and-debounce pattern in
      // useOrderPreview and useFiringSolution and every effect that resyncs a form from the
      // snapshot. Those are deliberate, and this build does not use the compiler.
      'react-hooks/rules-of-hooks': 'error',
      'react-hooks/exhaustive-deps': 'error',
      // A stray `any` is how a value off the wire or out of storage skips normalization.
      '@typescript-eslint/no-explicit-any': 'error',
      '@typescript-eslint/no-non-null-assertion': 'error',
    },
  },
);
