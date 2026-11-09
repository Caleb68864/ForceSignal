import js from '@eslint/js';
import reactHooks from 'eslint-plugin-react-hooks';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  { ignores: ['dist', 'dist-bench', 'node_modules', 'test-results', 'playwright-report'] },
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
  {
    // Playback and its effects present what the server resolved; they never work any of it out.
    // A shot's outcome is read off its FiringResult, compared at most, and never recomputed:
    // arithmetic on an outcome field, counting dice, a random number, or reaching for the rules
    // module is how a second opinion would creep in, and two devices could then disagree about a
    // shot both were sent. Held by src/lib/playback/noOutcome.test.ts, which lints a bad probe for
    // each selector so a typo here cannot leave the rule silently matching nothing.
    files: ['src/lib/playback/**/*.ts', 'src/components/map/pixi/**/*.{ts,tsx}'],
    ignores: ['**/*.test.{ts,tsx}'],
    rules: {
      'no-restricted-imports': ['error', {
        patterns: [{
          group: ['**/lib/rules*', '**/rules.ts', '**/weapons*', '**/fleetMath*', '**/useFiringSolution*'],
          message: 'Playback only presents server results. Rules live on the server.',
        }],
      }],
      'no-restricted-syntax': ['error',
        {
          selector: 'BinaryExpression[operator=/^([-+*/%]|\\*\\*)$/] > MemberExpression[property.name=/^(damage|armorDamageApplied|hullDamageApplied|rawDice|rangePenalty|screenReduction|systemPenalty|toHitNumber|hullDamage|armorDamage|hullMax|armorMax|effectiveScreens|screenRating|attackDice)$/]',
          message: 'No arithmetic on an outcome field: show the number the server sent.',
        },
        {
          selector: 'AssignmentExpression[operator=/^[-+*/%]=$/] > MemberExpression[property.name=/^(damage|armorDamageApplied|hullDamageApplied|hullDamage|armorDamage)$/]',
          message: 'No arithmetic on an outcome field: show the number the server sent.',
        },
        {
          selector: 'CallExpression[callee.object.property.name="diceRolls"][callee.property.name=/^(filter|reduce|reduceRight|some|every|find|findIndex|findLast|sort|toSorted)$/]',
          message: 'Do not count or judge dice: the server already did, and its damage field says what they scored.',
        },
        {
          selector: 'BinaryExpression > MemberExpression[computed=true][object.property.name="diceRolls"]',
          message: 'Do not compare a die to a target number: the server already did.',
        },
        {
          selector: 'CallExpression[callee.object.name="Math"][callee.property.name="random"]',
          message: 'No randomness in playback: every device must draw the same turn. Seed from the event.',
        },
      ],
    },
  },
);
